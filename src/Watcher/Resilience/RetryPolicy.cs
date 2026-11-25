namespace RobloxAutoManager.Watcher.Resilience;

using System.Net;
using System.Net.Http.Headers;
using RobloxAutoManager.Core.Models;

public sealed record RetryOptions(
    int MaxAttempts = 5,
    TimeSpan BaseDelay = default,
    TimeSpan MaxDelay = default,
    double JitterRatio = 0.30)
{
    public TimeSpan EffectiveBase => BaseDelay == default ? TimeSpan.FromMilliseconds(500) : BaseDelay;
    public TimeSpan EffectiveMax => MaxDelay == default ? TimeSpan.FromSeconds(60) : MaxDelay;
}

public static class RetryPolicy
{
    private static readonly HttpStatusCode[] TransientStatuses =
    {
        HttpStatusCode.RequestTimeout,          // 408
        HttpStatusCode.InternalServerError,     // 500
        HttpStatusCode.BadGateway,              // 502
        HttpStatusCode.ServiceUnavailable,      // 503
        HttpStatusCode.GatewayTimeout           // 504
    };

    public static ApiFailureKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => ApiFailureKind.RateLimited,
        HttpStatusCode.Unauthorized => ApiFailureKind.Unauthorized,
        HttpStatusCode.Forbidden => ApiFailureKind.Forbidden,
        >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => ApiFailureKind.BadRequest,
        _ when Array.IndexOf(TransientStatuses, status) >= 0 => ApiFailureKind.ServerError,
        _ => ApiFailureKind.Unknown
    };

    public static bool IsRetryable(ApiFailureKind kind) => kind is
        ApiFailureKind.RateLimited or
        ApiFailureKind.TransientNetwork or
        ApiFailureKind.ServerError;

    /// retry-after varsa otorite odur; yoksa full-jitter exponential backoff.
    public static TimeSpan ComputeDelay(int attempt, RetryOptions options, HttpResponseHeaders? headers, RateLimitSnapshot? snapshot)
    {
        if (headers?.RetryAfter is { } ra)
        {
            if (ra.Delta is { } delta && delta > TimeSpan.Zero)
                return Clamp(delta + Jitter(options), options);

            if (ra.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                    return Clamp(until + Jitter(options), options);
            }
        }

        if (snapshot is { IsExhausted: true } && snapshot.ResetIn > TimeSpan.Zero)
            return Clamp(snapshot.ResetIn + Jitter(options), options);

        double exponential = options.EffectiveBase.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var backoff = TimeSpan.FromMilliseconds(exponential);
        return Clamp(backoff + Jitter(options, backoff), options);
    }

    private static TimeSpan Jitter(RetryOptions options, TimeSpan? relativeTo = null)
    {
        double magnitude = (relativeTo ?? options.EffectiveBase).TotalMilliseconds * options.JitterRatio;
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * magnitude);
    }

    private static TimeSpan Clamp(TimeSpan value, RetryOptions options)
    {
        if (value < TimeSpan.Zero) return TimeSpan.Zero;
        return value > options.EffectiveMax ? options.EffectiveMax : value;
    }
}