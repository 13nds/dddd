// src/Watcher/CookieRefresher.cs
namespace RobloxAutoManager.Watcher;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;

public sealed class CookieRefresher : ICookieRefresher
{
    private readonly ILogger<CookieRefresher> _logger;
    private readonly Dictionary<string, DateTime> _lastRefresh = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    // Roblox CSRF token endpoint — cookie geçerliyse yeni token döner
    private const string CsrfEndpoint = "https://auth.roblox.com/v2/login";
    private const string ValidateEndpoint = "https://users.roblox.com/v1/users/authenticated";

    public CookieRefresher(ILogger<CookieRefresher> logger)
    {
        _logger = logger;
    }

    public bool NeedsRefresh(Account account)
    {
        if (!_lastRefresh.TryGetValue(account.Username, out var last))
            return true;
        return DateTime.UtcNow - last > RefreshInterval;
    }

    public async Task<string?> RefreshAsync(Account account, CancellationToken ct = default)
    {
        try
        {
            // 1. Mevcut cookie'yi validate et
            var validReq = RobloxHttpClient.CreateRequest(HttpMethod.Get, ValidateEndpoint, account.Cookie);
            var validResp = await RobloxHttpClient.Instance.SendAsync(validReq, ct);

            if (validResp.IsSuccessStatusCode)
            {
                // Cookie hala geçerli — sadece timestamp güncelle
                _lastRefresh[account.Username] = DateTime.UtcNow;
                _logger.LogDebug("Cookie still valid for {User}. Refresh timer reset.", account.Username);
                return account.Cookie;
            }

            // 2. Cookie expired — refresh dene
            // NOT: Roblox gerçek refresh için username/password veya refresh token ister.
            // Bu implementasyon cookie validity check + cache mekanizmasıdır.
            // Tam refresh için external auth provider entegrasyonu gerekir (Phase 4).
            
            _logger.LogWarning("Cookie expired for {User}. Manual re-login required.", account.Username);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cookie refresh failed for {User}", account.Username);
            return null;
        }
    }

    public void MarkRefreshed(string username)
    {
        _lastRefresh[username] = DateTime.UtcNow;
    }
}