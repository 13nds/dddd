// src/Watcher/Auth/CookieJar.cs
namespace RobloxAutoManager.Watcher.Auth;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public sealed record CookieRotation(string Username, string OldCookie, string NewCookie, DateTimeOffset At);

public sealed class CookieJar
{
    public const string CookieName = ".ROBLOSECURITY";

    /// Eski format prefix'i. Yeni formatlar gelebilir — prefix'e göre doğrulama YAPMA.
    private const string LegacyWarningPrefix =
        "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_";

    private readonly ConcurrentDictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _updatedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CookieJar> _logger;
    private readonly ILogService? _logs;

    public event Action<CookieRotation>? Rotated;

    public CookieJar(ILogger<CookieJar> logger, ILogService? logs = null)
    {
        _logger = logger;
        _logs = logs;
    }

    public void Set(string username, string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return;

        string normalized = Normalize(cookie);
        _cookies[username] = normalized;
        _updatedAt[username] = DateTimeOffset.UtcNow;
    }

    public string? Get(string username)
        => _cookies.TryGetValue(username, out var c) ? c : null;

    public DateTimeOffset GetUpdatedAt(string username)
        => _updatedAt.TryGetValue(username, out var t) ? t : DateTimeOffset.MinValue;

    public void Remove(string username)
    {
        _cookies.TryRemove(username, out _);
        _updatedAt.TryRemove(username, out _);
    }

    /// Response header'larını tara, .ROBLOSECURITY rotasyonu varsa yakala ve uygula.
    /// Roblox bu header'ı her istekte değil, saatler-günler arası düzensiz gönderir.
    public bool TryApplyResponse(string username, HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Set-Cookie", out var setCookies))
            return false;

        string? latest = null;

        foreach (var raw in setCookies)
        {
            if (TryExtractRoblosecurity(raw, out var value))
                latest = value; // aynı response'ta birden fazla varsa sonuncusu kazanır
        }

        if (latest is null) return false;

        string normalized = Normalize(latest);
        string? current = Get(username);

        if (string.Equals(current, normalized, StringComparison.Ordinal))
            return false;

        _cookies[username] = normalized;
        _updatedAt[username] = DateTimeOffset.UtcNow;

        var rotation = new CookieRotation(username, current ?? "", normalized, DateTimeOffset.UtcNow);

        _logger.LogInformation("Cookie rotated for {User}. Persisting.", username);
        _logs?.Log(LogLevel.Info, "CookieJar",
            "Set-Cookie rotation captured and applied", username);

        try { Rotated?.Invoke(rotation); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rotation subscriber threw for {User}", username);
        }

        return true;
    }

    /// Set-Cookie satırından sadece cookie değerini çıkar. Attribute'ları at.
    /// Örnek: ".ROBLOSECURITY=<value>; expires=...; domain=.roblox.com; path=/; HttpOnly; Secure"
    private static bool TryExtractRoblosecurity(string setCookieLine, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(setCookieLine)) return false;

        int firstSemi = setCookieLine.IndexOf(';');
        string pair = firstSemi >= 0 ? setCookieLine[..firstSemi] : setCookieLine;

        int eq = pair.IndexOf('=');
        if (eq <= 0) return false;

        string name = pair[..eq].Trim();
        if (!name.Equals(CookieName, StringComparison.OrdinalIgnoreCase)) return false;

        string val = pair[(eq + 1)..].Trim();

        // Silme yönergesi: boş değer veya expires geçmişte + value boş
        if (string.IsNullOrEmpty(val)) return false;

        // Tırnak içine alınmış olabilir
        if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
            val = val[1..^1];

        if (string.IsNullOrWhiteSpace(val)) return false;

        value = val;
        return true;
    }

    /// Kullanıcı panelden yapıştırırken başına/sonuna boşluk, newline, "Cookie:" öneki bulaşabilir.
    private static string Normalize(string cookie)
    {
        string c = cookie.Trim().Trim('"');

        // Yapıştırma kazası: ".ROBLOSECURITY=..." şeklinde geldiyse değeri al
        const string prefix = CookieName + "=";
        if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            c = c[prefix.Length..];

        // Satır sonları cookie'yi bozar
        c = c.Replace("\r", "").Replace("\n", "").Replace("\t", "");

        return c.Trim();
    }

    /// SADECE bilgilendirme amaçlı. Format doğrulaması için KULLANMA —
    /// Roblox yeni formatları önceden duyurmadan değiştirebilir.
    public static bool LooksLikeLegacyFormat(string cookie)
        => cookie.StartsWith(LegacyWarningPrefix, StringComparison.Ordinal);

    public IReadOnlyDictionary<string, DateTimeOffset> SnapshotTimestamps()
        => new Dictionary<string, DateTimeOffset>(_updatedAt);
}