namespace RobloxAutoManager.Watcher.RateLimiting;

using RobloxAutoManager.Core.Models;

internal sealed class AdaptiveTokenBucket
{
    private readonly object _gate = new();
    private double _tokens;
    private double _capacity;
    private double _refillPerSecond;
    private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

    private double _pressureEma;
    private const double EmaAlpha = 0.25;
    private const double MinCapacityFloor = 1;

    public string Key { get; }

    public AdaptiveTokenBucket(string key, double initialCapacity = 10, double refillPerSecond = 2)
    {
        Key = key;
        _capacity = initialCapacity;
        _tokens = initialCapacity;
        _refillPerSecond = refillPerSecond;
    }

    public void ApplySnapshot(RateLimitSnapshot snapshot)
    {
        lock (_gate)
        {
            if (snapshot.Windows.Count > 0)
            {
                // En kısıtlayıcı pencereyi temel al
                var tightest = snapshot.Windows
                    .OrderBy(w => w.Limit / (double)Math.Max(1, w.WindowSeconds))
                    .First();

                double observedRate = tightest.Limit / (double)Math.Max(1, tightest.WindowSeconds);

                // Güvenlik marjı: gözlenen oranın %70'i
                _refillPerSecond = Math.Max(0.05, observedRate * 0.70);
                _capacity = Math.Max(MinCapacityFloor, Math.Min(tightest.Limit * 0.25, 50));
            }

            _pressureEma = (_pressureEma * (1 - EmaAlpha)) + (snapshot.Pressure * EmaAlpha);

            // Baskı yüksekse kapasiteyi kıs — AIMD multiplicative decrease
            if (_pressureEma > 0.75)
                _capacity = Math.Max(MinCapacityFloor, _capacity * 0.6);
            else if (_pressureEma < 0.25)
                _capacity = Math.Min(50, _capacity + 1); // additive increase

            if (snapshot.IsExhausted)
                _cooldownUntil = DateTimeOffset.UtcNow + snapshot.ResetIn + TimeSpan.FromMilliseconds(250);

            _tokens = Math.Min(_tokens, _capacity);
        }
    }

    public void Penalize(TimeSpan cooldown)
    {
        lock (_gate)
        {
            var until = DateTimeOffset.UtcNow + cooldown;
            if (until > _cooldownUntil) _cooldownUntil = until;

            _capacity = Math.Max(MinCapacityFloor, _capacity * 0.5);
            _tokens = 0;
        }
    }

    public TimeSpan GetWaitTime()
    {
        lock (_gate)
        {
            Refill();

            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
                return _cooldownUntil - now;

            if (_tokens >= 1.0)
                return TimeSpan.Zero;

            double deficit = 1.0 - _tokens;
            return TimeSpan.FromSeconds(deficit / Math.Max(0.001, _refillPerSecond));
        }
    }

    public bool TryConsume()
    {
        lock (_gate)
        {
            Refill();
            if (DateTimeOffset.UtcNow < _cooldownUntil) return false;
            if (_tokens < 1.0) return false;
            _tokens -= 1.0;
            return true;
        }
    }

    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        double elapsed = (now - _lastRefill).TotalSeconds;
        if (elapsed <= 0) return;

        _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillPerSecond));
        _lastRefill = now;
    }
}