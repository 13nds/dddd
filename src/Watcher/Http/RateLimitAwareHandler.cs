namespace RobloxAutoManager.Watcher.Http;

using RobloxAutoManager.Core.Services;

public sealed class RateLimitAwareHandler : DelegatingHandler
{
    private readonly IRateLimiter _limiter;
    private const string UsernameHeader = "X-RAM-Account";

    public RateLimitAwareHandler(IRateLimiter limiter)
        : base(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 8,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            EnableMultipleHttp2Connections = true
        })
    {
        _limiter = limiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? username = null;
        if (request.Headers.TryGetValues(UsernameHeader, out var vals))
        {
            username = vals.FirstOrDefault();
            request.Headers.Remove(UsernameHeader); // wire'a sızmaz
        }

        string bucket = RobloxApiPipeline.BucketFor(request.RequestUri?.Host ?? "unknown", username);

        using var lease = await _limiter.AcquireAsync(bucket, ct);
        var response = await base.SendAsync(request, ct);
        _limiter.ObserveHeaders(bucket, response.Headers);

        if ((int)response.StatusCode == 429)
        {
            var snapshot = _limiter.GetSnapshot(bucket);
            var cooldown = response.Headers.RetryAfter?.Delta
                           ?? snapshot?.ResetIn
                           ?? TimeSpan.FromSeconds(5);
            _limiter.Penalize(bucket, cooldown);
        }

        return response;
    }
}