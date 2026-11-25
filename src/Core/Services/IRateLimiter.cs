namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface IRateLimiter
{
    Task<IDisposable> AcquireAsync(string bucketKey, CancellationToken ct = default);
    void ObserveHeaders(string bucketKey, System.Net.Http.Headers.HttpResponseHeaders headers);
    void Penalize(string bucketKey, TimeSpan cooldown);
    RateLimitSnapshot? GetSnapshot(string bucketKey);
    IReadOnlyDictionary<string, RateLimitSnapshot> GetAllSnapshots();
}