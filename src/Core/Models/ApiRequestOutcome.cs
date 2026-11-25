namespace RobloxAutoManager.Core.Models;

public enum ApiFailureKind
{
    None,
    RateLimited,
    TransientNetwork,
    ServerError,
    Unauthorized,
    Forbidden,
    BadRequest,
    CircuitOpen,
    Cancelled,
    Unknown
}

public sealed record ApiRequestOutcome<T>(
    bool Success,
    T? Value,
    int StatusCode,
    ApiFailureKind Failure,
    string? ErrorMessage,
    int Attempts,
    TimeSpan Elapsed,
    RateLimitSnapshot? RateLimit)
{
    public static ApiRequestOutcome<T> Ok(T value, int status, int attempts, TimeSpan elapsed, RateLimitSnapshot? rl)
        => new(true, value, status, ApiFailureKind.None, null, attempts, elapsed, rl);

    public static ApiRequestOutcome<T> Fail(int status, ApiFailureKind kind, string? msg, int attempts, TimeSpan elapsed, RateLimitSnapshot? rl)
        => new(false, default, status, kind, msg, attempts, elapsed, rl);
}