namespace RobloxAutoManager.Watcher.Resilience;

using Microsoft.Extensions.Logging;

public enum CircuitState { Closed, Open, HalfOpen }

public sealed class CircuitBreaker
{
    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly int _halfOpenProbes;

    private int _consecutiveFailures;
    private int _halfOpenSuccesses;
    private DateTimeOffset _openedAt = DateTimeOffset.MinValue;

    public string Key { get; }
    public CircuitState State { get; private set; } = CircuitState.Closed;

    public CircuitBreaker(string key, ILogger logger, int failureThreshold = 5, TimeSpan? openDuration = null, int halfOpenProbes = 2)
    {
        Key = key;
        _logger = logger;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _halfOpenProbes = halfOpenProbes;
    }

    public bool AllowRequest()
    {
        lock (_gate)
        {
            if (State == CircuitState.Closed) return true;

            if (State == CircuitState.Open)
            {
                if (DateTimeOffset.UtcNow - _openedAt >= _openDuration)
                {
                    State = CircuitState.HalfOpen;
                    _halfOpenSuccesses = 0;
                    _logger.LogInformation("Circuit {Key} → HalfOpen", Key);
                    return true;
                }
                return false;
            }

            return true; // HalfOpen: probe'a izin ver
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;

            if (State == CircuitState.HalfOpen)
            {
                _halfOpenSuccesses++;
                if (_halfOpenSuccesses >= _halfOpenProbes)
                {
                    State = CircuitState.Closed;
                    _logger.LogInformation("Circuit {Key} → Closed (recovered)", Key);
                }
            }
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;

            if (State == CircuitState.HalfOpen || _consecutiveFailures >= _failureThreshold)
            {
                State = CircuitState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning("Circuit {Key} → Open for {Seconds}s", Key, _openDuration.TotalSeconds);
            }
        }
    }

    public TimeSpan RemainingOpenTime
    {
        get
        {
            lock (_gate)
            {
                if (State != CircuitState.Open) return TimeSpan.Zero;
                var remaining = _openDuration - (DateTimeOffset.UtcNow - _openedAt);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}