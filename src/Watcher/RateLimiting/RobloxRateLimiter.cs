namespace RobloxAutoManager.Watcher.RateLimiting;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public sealed class RobloxRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, AdaptiveTokenBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _concurrency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RateLimitSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<RobloxRateLimiter> _logger;
    private readonly ILogService? _logService;

    private const int MaxParallelPerBucket = 3;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);

    public RobloxRateLimiter(ILogger<RobloxRateLimiter> logger, ILogService? logService = null)
    {
        _logger = logger;
        _logService = logService;
    }

    public async Task<IDisposable> AcquireAsync(string bucketKey, CancellationToken ct = default)
    {
        var bucket = _buckets.GetOrAdd(bucketKey, k => new AdaptiveTokenBucket(k));
        var gate = _concurrency.GetOrAdd(bucketKey, _ => new SemaphoreSlim(MaxParallelPerBucket, MaxParallelPerBucket));

        await gate.WaitAsync(ct);
        try
        {
            int spins = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (bucket.TryConsume())
                    return new Releaser(gate);

                var wait = bucket.GetWaitTime();
                if (wait > TimeSpan.FromSeconds(2) && spins % 25 == 0)
                {
                    _logger.LogDebug("Bucket {Bucket} throttled for {Wait:F1}s", bucketKey, wait.TotalSeconds);
                    _logService?.Log(LogLevel.Debug, "RateLimiter",
                        $"Bucket throttled {wait.TotalSeconds:F1}s", bucketKey);
                }

                var delay = wait < PollInterval ? PollInterval : (wait > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : wait);
                await Task.Delay(delay, ct);
                spins++;
            }
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public void ObserveHeaders(string bucketKey, System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var snapshot = RateLimitSnapshot.FromHeaders(bucketKey, headers);
        _snapshots[bucketKey] = snapshot;
        _buckets.GetOrAdd(bucketKey, k => new AdaptiveTokenBucket(k)).ApplySnapshot(snapshot);
    }

    public void Penalize(string bucketKey, TimeSpan cooldown)
    {
        _buckets.GetOrAdd(bucketKey, k => new AdaptiveTokenBucket(k)).Penalize(cooldown);
        _logger.LogWarning("Bucket {Bucket} penalized for {Seconds:F1}s", bucketKey, cooldown.TotalSeconds);
        _logService?.Log(LogLevel.Warning, "RateLimiter",
            $"Cooldown applied: {cooldown.TotalSeconds:F1}s", bucketKey);
    }

    public RateLimitSnapshot? GetSnapshot(string bucketKey)
        => _snapshots.TryGetValue(bucketKey, out var s) ? s : null;

    public IReadOnlyDictionary<string, RateLimitSnapshot> GetAllSnapshots()
        => new Dictionary<string, RateLimitSnapshot>(_snapshots);

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}