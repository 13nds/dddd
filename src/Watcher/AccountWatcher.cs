namespace RobloxAutoManager.Watcher;

using System.Collections.Concurrent;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;
using Microsoft.Extensions.Logging;

public sealed class AccountWatcher : IAccountWatcher
{
    private readonly ConcurrentDictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRobloxLauncher _launcher;
    private readonly RejoinStrategy _rejoin;
    private readonly ILogger<AccountWatcher> _logger;
    private CancellationTokenSource? _cts;
    private Task? _watchLoop;

    public event Action<Account>? AccountStateChanged;

    public IReadOnlyList<Account> Accounts => _accounts.Values.ToList();

    public AccountWatcher(
        IRobloxLauncher launcher,
        RejoinStrategy rejoin,
        ILogger<AccountWatcher> logger)
    {
        _launcher = launcher;
        _rejoin = rejoin;
        _logger = logger;
    }

    public Task AddAccountAsync(Account account, CancellationToken ct = default)
    {
        _accounts[account.Username] = account;
        _logger.LogInformation("Added account: {User}", account.Username);
        NotifyStateChange(account);
        return Task.CompletedTask;
    }

    public Task RemoveAccountAsync(string username, CancellationToken ct = default)
    {
        if (_accounts.TryRemove(username, out var removed))
        {
            _rejoin.ResetCounter(username);
            _logger.LogInformation("Removed account: {Username}", username);
            NotifyStateChange(removed with { State = AccountState.Offline });
        }
        return Task.CompletedTask;
    }

    public Task UpdateServerConfigAsync(string username, ServerConfig config, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(username, out var acc))
        {
            _accounts[username] = acc with { TargetServer = config };
            _logger.LogInformation("Updated server config for {User}: Place={Place}, Job={Job}",
                username, config.PlaceId, config.JobId ?? "none");
        }
        return Task.CompletedTask;
    }

    public Task StartWatchingAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _watchLoop = Task.Run(() => WatchLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("Account watcher started. Monitoring {Count} accounts.", _accounts.Count);
        return Task.CompletedTask;
    }

    public async Task StopWatchingAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_watchLoop != null)
                await _watchLoop.WaitAsync(ct);
            _cts.Dispose();
            _cts = null;
        }
        _logger.LogInformation("Account watcher stopped.");
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var kvp in _accounts)
            {
                if (ct.IsCancellationRequested) break;

                var account = kvp.Value;
                bool running = await _launcher.IsRunningAsync(account.Username, ct);

                AccountState newState = running ? AccountState.InGame : AccountState.Offline;

                if (newState != account.State)
                {
                    var updated = account with { State = newState, LastChecked = DateTime.UtcNow };
                    _accounts[account.Username] = updated;
                    NotifyStateChange(updated);

                    if (newState == AccountState.Offline && account.State == AccountState.InGame)
                    {
                        // Crash / disconnect detected → auto-rejoin
                        _ = _rejoin.HandleDisconnectAsync(updated, ct);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private void NotifyStateChange(Account account)
    {
        try { AccountStateChanged?.Invoke(account); }
        catch (Exception ex) { _logger.LogError(ex, "State change handler threw"); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync();
    }
}