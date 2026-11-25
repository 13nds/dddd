namespace RobloxAutoManager.Watcher;

using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;
using Microsoft.Extensions.Logging;

public sealed class RejoinStrategy
{
    private readonly IRobloxLauncher _launcher;
    private readonly ILogger<RejoinStrategy> _logger;
    private readonly Dictionary<string, int> _failureCounters = new();
    private const int MaxRetries = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(10);

    public RejoinStrategy(IRobloxLauncher launcher, ILogger<RejoinStrategy> logger)
    {
        _launcher = launcher;
        _logger = logger;
    }

    public async Task HandleDisconnectAsync(Account account, CancellationToken ct = default)
    {
        string key = account.Username;
        _failureCounters.TryGetValue(key, out int failures);

        if (failures >= MaxRetries)
        {
            _logger.LogWarning("{User} reached max retries ({Max}). Marking as Error.", key, MaxRetries);
            _failureCounters[key] = 0; // Reset for next manual trigger
            return;
        }

        _failureCounters[key] = failures + 1;
        _logger.LogInformation("Rejoin attempt {Attempt}/{Max} for {User}", failures + 1, MaxRetries, key);

        await Task.Delay(_retryDelay, ct);
        bool launched = await _launcher.LaunchAsync(account, ct);

        if (!launched)
        {
            _logger.LogError("Failed to launch {User} on retry {Attempt}", key, failures + 1);
        }
    }

    public void ResetCounter(string username) => _failureCounters.Remove(username);
}