// src/Watcher/OAuthManager.cs  (REPLACES previous version entirely)
namespace RobloxAutoManager.Watcher;

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher.Http;

public sealed class OAuthManager
{
    private readonly ITokenStore _store;
    private readonly RobloxApiPipeline _api;
    private readonly ILogger<OAuthManager> _logger;
    private readonly ILogService _logs;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private const string AuthorizeEndpoint = "https://apis.roblox.com/oauth/v1/authorize";
    private const string TokenEndpoint = "https://apis.roblox.com/oauth/v1/token";
    private const string UserInfoEndpoint = "https://apis.roblox.com/oauth/v1/userinfo";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    public OAuthManager(
        ITokenStore store,
        RobloxApiPipeline api,
        ILogService logs,
        ILogger<OAuthManager> logger,
        OAuthClientConfig config)
    {
        _store = store;
        _api = api;
        _logs = logs;
        _logger = logger;
        _clientId = config.ClientId;
        _clientSecret = config.ClientSecret;
        _redirectUri = config.RedirectUri;
    }

    public async Task<OAuthToken?> GetValidTokenAsync(string username, CancellationToken ct = default)
    {
        var token = await _store.LoadAsync(username, ct);
        if (token is null)
        {
            _logs.Log(LogLevel.Warning, "OAuth", "No stored token — login required", username);
            return null;
        }

        if (!token.IsExpired) return token;

        if (!token.CanRefresh)
        {
            _logs.Log(LogLevel.Error, "OAuth", "Expired with no refresh token — re-auth required", username);
            return null;
        }

        // Tek uçuş: aynı hesap için eşzamanlı refresh rotation'ı bozar
        await _refreshGate.WaitAsync(ct);
        try
        {
            // Double-check: başka bir çağrı zaten yenilemiş olabilir
            token = await _store.LoadAsync(username, ct) ?? token;
            if (!token.IsExpired) return token;

            var refreshed = await RefreshTokenAsync(token, ct);
            if (refreshed is null) return null;

            await _store.SaveAsync(refreshed, ct);
            _logs.Log(LogLevel.Info, "OAuth",
                $"Token rotated. Valid until {refreshed.ExpiresAt:HH:mm:ss}", username);
            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<OAuthToken?> RefreshTokenAsync(OAuthToken old, CancellationToken ct)
    {
        var outcome = await _api.SendAsync<TokenResponse>(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = old.RefreshToken!,
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret
                })
            };
            req.Headers.Add("X-RAM-Account", old.Username);
            return req;
        }, old.Username, ct: ct);

        if (!outcome.Success || outcome.Value is null)
        {
            _logs.Log(LogLevel.Error, "OAuth",
                $"Refresh failed ({outcome.StatusCode} / {outcome.Failure})", old.Username, outcome.ErrorMessage);

            // Refresh token tek kullanımlık: başarısızlıkta eskisini tekrar denemek hesabı kilitler.
            if (outcome.StatusCode is 400 or 401)
                await _store.DeleteAsync(old.Username, ct);

            return null;
        }

        var r = outcome.Value;

        // ROTATION ZORUNLU — yeni refresh token yoksa oturum geçersizdir, eskisini tutma.
        if (string.IsNullOrEmpty(r.RefreshToken))
        {
            _logs.Log(LogLevel.Fatal, "OAuth",
                "Rotation violated: no refresh_token in response. Session invalidated.", old.Username);
            await _store.DeleteAsync(old.Username, ct);
            return null;
        }

        return new OAuthToken(
            AccessToken: r.AccessToken,
            RefreshToken: r.RefreshToken,
            ExpiresAt: DateTime.UtcNow.AddSeconds(r.ExpiresIn),
            Username: old.Username,
            UserId: old.UserId
        );
    }

    public async Task<OAuthToken?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        var outcome = await _api.SendAsync<TokenResponse>(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier,
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["redirect_uri"] = _redirectUri
                })
            };
            return req;
        }, ct: ct);

        if (!outcome.Success || outcome.Value is null) return null;

        var info = await _api.SendAsync<UserInfoResponse>(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outcome.Value.AccessToken);
            return req;
        }, ct: ct);

        string userId = info.Value?.Sub ?? "unknown";
        string username = info.Value?.PreferredUsername ?? userId;

        var token = new OAuthToken(
            AccessToken: outcome.Value.AccessToken,
            RefreshToken: outcome.Value.RefreshToken,
            ExpiresAt: DateTime.UtcNow.AddSeconds(outcome.Value.ExpiresIn),
            Username: username,
            UserId: userId
        );

        await _store.SaveAsync(token, ct);
        _logs.Log(LogLevel.Info, "OAuth", $"Authorization complete. UserId={userId}", username);
        return token;
    }

    public (string Url, string CodeVerifier, string State) GenerateAuthUrl(string scope = "openid profile")
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Guid.NewGuid().ToString("N");

        string url = $"{AuthorizeEndpoint}" +
                     $"?response_type=code" +
                     $"&client_id={Uri.EscapeDataString(_clientId)}" +
                     $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
                     $"&scope={Uri.EscapeDataString(scope)}" +
                     $"&state={state}" +
                     $"&code_challenge={challenge}" +
                     $"&code_challenge_method=S256";

        return (url, verifier, state);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("preferred_username")] string? PreferredUsername);
}

public sealed record OAuthClientConfig(string ClientId, string ClientSecret, string RedirectUri);