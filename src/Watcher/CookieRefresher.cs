// src/Watcher/CookieRefresher.cs
namespace RobloxAutoManager.Watcher;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher.Auth;

public sealed class CookieRefresher : ICookieRefresher
{
    private readonly RobloxAuthClient _auth;
    private readonly CookieJar _jar;
    private readonly ILogger<CookieRefresher> _logger;
    private readonly ILogService _logs;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRefresh =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public CookieRefresher(
        RobloxAuthClient auth,
        CookieJar jar,
        ILogService logs,
        ILogger<CookieRefresher> logger)
    {
        _auth = auth;
        _jar = jar;
        _logs = logs;
        _logger = logger;
    }

    public bool NeedsRefresh(Account account)
    {
        if (!_lastRefresh.TryGetValue(account.Username, out var last))
            return true;

        return DateTimeOffset.UtcNow - last > RefreshInterval;
    }

    /// Güncel cookie'yi döner. Ölüyse null.
    /// POST /v1/session/refresh Set-Cookie'yi zorla tetikler — Roblox staff workaround.
    public async Task<string?> RefreshAsync(Account account, CancellationToken ct = default)
    {
        string seed = _jar.Get(account.Username) ?? account.Cookie;

        if (string.IsNullOrWhiteSpace(seed))
        {
            _logs.Log(LogLevel.Warning, "Cookie", "No cookie available", account.Username);
            return null;
        }

        _jar.Set(account.Username, seed);

        bool ok = await _auth.ForceSessionRefreshAsync(account.Username, ct);

        if (!ok)
        {
            // Refresh başarısız — cookie hâlâ geçerli olabilir, validate ile teyit et
            var check = await _auth.ValidateAsync(account.Username, seed, ct);

            if (!check.Success)
            {
                _logs.Log(LogLevel.Error, "Cookie",
                    $"Cookie dead ({check.Health}). Re-auth required.", account.Username, check.Error);
                return null;
            }
        }

        _lastRefresh[account.Username] = DateTimeOffset.UtcNow;

        string current = _jar.Get(account.Username) ?? seed;

        if (!string.Equals(current, account.Cookie, StringComparison.Ordinal))
        {
            _logs.Log(LogLevel.Info, "Cookie",
                "Cookie rotated during refresh cycle", account.Username);
        }

        return current;
    }

    public void MarkRefreshed(string username)
        => _lastRefresh[username] = DateTimeOffset.UtcNow;

    public void Forget(string username)
    {
        _lastRefresh.TryRemove(username, out _);
        _jar.Remove(username);
    }
}