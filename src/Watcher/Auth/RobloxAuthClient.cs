// src/Watcher/Auth/RobloxAuthClient.cs
namespace RobloxAutoManager.Watcher.Auth;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher.Http;

public sealed record AuthValidationResult(
    bool Success,
    long UserId,
    string Username,
    string? DisplayName,
    SessionHealth Health,
    string? Error,
    bool CookieWasRotated);

public sealed class RobloxAuthClient
{
    private const string AuthenticatedUserEndpoint = "https://users.roblox.com/v1/users/authenticated";

    /// Roblox staff'ın verdiği resmi workaround: bu endpoint Set-Cookie'yi ZORLA döndürür.
    /// Body yok, POST, authenticated. Bazı endpoint'ler rotasyon header'ı hiç göndermiyor.
    private const string SessionRefreshEndpoint = "https://auth.roblox.com/v1/session/refresh";

    private const string CsrfHeader = "x-csrf-token";
    private const string AccountHeader = "X-RAM-Account";

    private readonly RobloxApiPipeline _api;
    private readonly CookieJar _jar;
    private readonly ILogService _logs;
    private readonly ILogger<RobloxAuthClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RobloxAuthClient(
        RobloxApiPipeline api,
        CookieJar jar,
        ILogService logs,
        ILogger<RobloxAuthClient> logger)
    {
        _api = api;
        _jar = jar;
        _logs = logs;
        _logger = logger;
    }

    /// Cookie'yi doğrula, userId/username çek. Rotasyon varsa jar'a işle.
    public async Task<AuthValidationResult> ValidateAsync(
        string username, string cookie, CancellationToken ct = default)
    {
        _jar.Set(username, cookie);
        string effective = _jar.Get(username) ?? cookie;
        bool rotated = false;

        var outcome = await SendAuthenticatedAsync<AuthenticatedUserResponse>(
            HttpMethod.Get,
            AuthenticatedUserEndpoint,
            username,
            () => effective,
            csrfToken: null,
            body: null,
            onRotated: () => rotated = true,
            ct);

        if (!outcome.Success || outcome.Value is null)
        {
            var health = outcome.StatusCode switch
            {
                401 => SessionHealth.CookieInvalid,
                403 => SessionHealth.CookieInvalid,
                429 => SessionHealth.RateLimited,
                0 => SessionHealth.NetworkDown,
                _ => SessionHealth.Unknown
            };

            _logs.Log(LogLevel.Warning, "Auth",
                $"Validation failed ({outcome.StatusCode} / {outcome.Failure})", username, outcome.ErrorMessage);

            return new AuthValidationResult(false, 0, username, null, health,
                outcome.ErrorMessage ?? $"HTTP {outcome.StatusCode}", rotated);
        }

        var u = outcome.Value;
        _logs.Log(LogLevel.Info, "Auth",
            $"Cookie valid. UserId={u.Id}", u.Name ?? username);

        return new AuthValidationResult(true, u.Id, u.Name ?? username, u.DisplayName,
            SessionHealth.Healthy, null, rotated);
    }

    /// Rotasyonu zorla tetikle. Periyodik çağrılmalı — cookie yaşlanmadan yenilenir.
    public async Task<bool> ForceSessionRefreshAsync(string username, CancellationToken ct = default)
    {
        string? cookie = _jar.Get(username);
        if (string.IsNullOrEmpty(cookie))
        {
            _logs.Log(LogLevel.Warning, "Auth", "No cookie in jar, cannot refresh", username);
            return false;
        }

        bool rotated = false;

        var outcome = await SendAuthenticatedAsync<JsonElement>(
            HttpMethod.Post,
            SessionRefreshEndpoint,
            username,
            () => _jar.Get(username) ?? cookie,
            csrfToken: null,
            body: null,
            onRotated: () => rotated = true,
            ct);

        if (outcome.StatusCode == 401)
        {
            _logs.Log(LogLevel.Error, "Auth",
                "Session refresh returned 401 — cookie dead, re-auth required", username);
            return false;
        }

        if (!outcome.Success && outcome.Failure is not ApiFailureKind.None)
        {
            _logs.Log(LogLevel.Warning, "Auth",
                $"Session refresh failed ({outcome.StatusCode})", username, outcome.ErrorMessage);
            return false;
        }

        _logs.Log(rotated ? LogLevel.Info : LogLevel.Debug, "Auth",
            rotated ? "Session refreshed, new cookie stored" : "Session refresh OK, no rotation issued",
            username);

        return true;
    }

    /// Cookie'li istek + otomatik CSRF retry.
    /// 403 + x-csrf-token response header gelirse isteği o token'la bir kez tekrar gönder.
    private async Task<ApiRequestOutcome<T>> SendAuthenticatedAsync<T>(
        HttpMethod method,
        string url,
        string username,
        Func<string> cookieAccessor,
        string? csrfToken,
        HttpContent? body,
        Action? onRotated,
        CancellationToken ct)
    {
        string? activeCsrf = csrfToken;
        const int maxCsrfRetries = 2;

        for (int csrfAttempt = 0; csrfAttempt <= maxCsrfRetries; csrfAttempt++)
        {
            string? capturedCsrf = null;

            var outcome = await _api.SendAsync<T>(() =>
            {
                var req = new HttpRequestMessage(method, url);

                req.Headers.TryAddWithoutValidation("Cookie", $"{CookieJar.CookieName}={cookieAccessor()}");
                req.Headers.TryAddWithoutValidation(AccountHeader, username);
                req.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
                req.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");

                if (!string.IsNullOrEmpty(activeCsrf))
                    req.Headers.TryAddWithoutValidation(CsrfHeader, activeCsrf);

                if (body is not null)
                    req.Content = body;

                return req;
            },
            username,
            onHeaders: headers =>
            {
                if (_jar.TryApplyResponse(username, headers))
                    onRotated?.Invoke();

                if (headers.TryGetValues(CsrfHeader, out var tokens))
                    capturedCsrf = tokens.FirstOrDefault();
            },
            ct: ct);

            bool rotatedNow = !string.Equals(cookieAccessor(), _jar.Get(username), StringComparison.Ordinal);
            if (rotatedNow) onRotated?.Invoke();

            // CSRF challenge: 403 + token geldiyse tekrar dene
            if (outcome.StatusCode == 403 && !string.IsNullOrEmpty(capturedCsrf) && csrfAttempt < maxCsrfRetries)
            {
                _logs.Log(LogLevel.Debug, "Auth",
                    $"CSRF challenge received, retrying with token (attempt {csrfAttempt + 1})", username);

                activeCsrf = capturedCsrf;
                continue;
            }

            return outcome;
        }

        return ApiRequestOutcome<T>.Fail(403, ApiFailureKind.Forbidden,
            "CSRF retry budget exhausted", maxCsrfRetries + 1, TimeSpan.Zero, null);
    }

    private sealed record AuthenticatedUserResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("displayName")] string? DisplayName);
}