// src/Core/Models/RobloxSession.cs
namespace RobloxAutoManager.Core.Models;

/// Roblox presence API numerik değerleri: Offline=0, Online=1, InGame=2, InStudio=3, Invisible=4
public enum RobloxPresenceType
{
    Offline = 0,
    Online = 1,
    InGame = 2,
    InStudio = 3,
    Invisible = 4
}

public sealed record PresenceSnapshot(
    long UserId,
    RobloxPresenceType Type,
    string? LastLocation,
    long? PlaceId,
    long? RootPlaceId,
    string? GameId,
    long? UniverseId,
    DateTimeOffset? LastOnline,
    DateTimeOffset FetchedAt)
{
    public bool IsPlaying => Type == RobloxPresenceType.InGame;

    public bool IsInTargetPlace(long targetPlaceId)
        => IsPlaying && (PlaceId == targetPlaceId || RootPlaceId == targetPlaceId);

    public bool IsInTargetServer(string targetJobId)
        => IsPlaying && string.Equals(GameId, targetJobId, StringComparison.OrdinalIgnoreCase);

    public TimeSpan Age => DateTimeOffset.UtcNow - FetchedAt;

    public static PresenceSnapshot Unknown(long userId) => new(
        userId, RobloxPresenceType.Offline, null, null, null, null, null, null, DateTimeOffset.MinValue);
}

public enum SessionHealth
{
    Unknown,
    Healthy,
    CookieRotated,
    CookieInvalid,
    RateLimited,
    NetworkDown,
    RequiresReauth
}

public sealed record RobloxSession
{
    public required string Username { get; init; }
    public required long UserId { get; init; }

    /// Ham .ROBLOSECURITY değeri. Set-Cookie ile rotate edilir, asla sabit tutulmaz.
    public required string Cookie { get; init; }

    public string? CsrfToken { get; init; }
    public OAuthToken? OAuth { get; init; }

    public SessionHealth Health { get; init; } = SessionHealth.Unknown;
    public DateTimeOffset CookieUpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastValidatedAt { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastRefreshAttemptAt { get; init; } = DateTimeOffset.MinValue;

    public PresenceSnapshot? Presence { get; init; }

    public int ConsecutiveAuthFailures { get; init; }
    public string? LastError { get; init; }

    public bool CanAuthenticate => !string.IsNullOrWhiteSpace(Cookie) && Health != SessionHealth.RequiresReauth;

    public TimeSpan CookieAge => DateTimeOffset.UtcNow - CookieUpdatedAt;

    public RobloxSession WithCookie(string newCookie) => this with
    {
        Cookie = newCookie,
        CookieUpdatedAt = DateTimeOffset.UtcNow,
        Health = SessionHealth.CookieRotated,
        ConsecutiveAuthFailures = 0,
        LastError = null
    };

    public RobloxSession WithCsrf(string token) => this with { CsrfToken = token };

    public RobloxSession WithPresence(PresenceSnapshot snapshot) => this with { Presence = snapshot };

    public RobloxSession WithHealth(SessionHealth health, string? error = null) => this with
    {
        Health = health,
        LastError = error,
        ConsecutiveAuthFailures = health is SessionHealth.CookieInvalid or SessionHealth.RequiresReauth
            ? ConsecutiveAuthFailures + 1
            : 0
    };

    public RobloxSession MarkValidated() => this with
    {
        LastValidatedAt = DateTimeOffset.UtcNow,
        Health = SessionHealth.Healthy,
        ConsecutiveAuthFailures = 0,
        LastError = null
    };
}