// src/Watcher/AccountWatcher.cs
namespace RobloxAutoManager.Watcher;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher.Auth;

public sealed class AccountWatcher : IAccountWatcher
{
    private readonly ConcurrentDictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRobloxLauncher _launcher;
    private readonly RejoinStrategy _rejoin;
    private readonly ICookieRefresher _refresher;
    private readonly CookieJar _jar;
    private readonly ILogService _logs;
    private readonly ILogger<AccountWatcher> _logger;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _watchLoop;
    private bool _disposed;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public event Action<Account>? AccountStateChanged;

    public IReadOnlyList<Account> Accounts => _accounts.Values.ToList();

    public AccountWatcher(
        IRobloxLauncher launcher,
        RejoinStrategy rejoin,
        ICookieRefresher refresher,
        CookieJar jar,
        ILogService logs,
        ILogger<AccountWatcher> logger)
    {
        _launcher = launcher;
        _rejoin = rejoin;
        _refresher = refresher;
        _jar = jar;
        _logs = logs;
        _logger = logger;
    }

    public Task AddAccountAsync(Account account, CancellationToken ct = default)
    {
        _accounts[account.Username] = account;

        if (account.HasCookie)
            _jar.Set(account.Username, account.Cookie);

        _logs.Log(LogLevel.Info, "Watcher", $"Account added (UserId={account.UserId})", account.Username);
        NotifyStateChange(account);
        return Task.CompletedTask;
    }

    public Task RemoveAccountAsync(string username, CancellationToken ct = default)
    {
        if (_accounts.TryRemove(username, out var removed))
        {
            _rejoin.ResetCounter(username);
            _jar.Remove(username);

            _logs.Log(LogLevel.Info, "Watcher", "Account removed", username);
            NotifyStateChange(removed with { State = AccountState.Offline, LastError = "__REMOVED__" });
        }
        return Task.CompletedTask;
    }

    public Task UpdateServerConfigAsync(string username, ServerConfig config, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(username, out var acc))
        {
            var updated = acc with { TargetServer = config };
            _accounts[username] = updated;

            _logs.Log(LogLevel.Info, "Watcher",
                $"Server config updated: Place={config.PlaceId}, Job={config.JobId ?? "none"}", username);

            NotifyStateChange(updated);
        }
        return Task.CompletedTask;
    }

    public async Task StartWatchingAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct);
        try
        {
            if (_cts is not null)
            {
                _logger.LogDebug("Watcher already running.");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _watchLoop = Task.Run(() => WatchLoopAsync(_cts.Token));

            _logs.Log(LogLevel.Info, "Watcher", $"Started. Monitoring {_accounts.Count} accounts.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopWatchingAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct);
        try
        {
            if (_cts is null) return;

            var loop = _watchLoop;
            var cts = _cts;

            _cts = null;
            _watchLoop = null;

            await cts.CancelAsync();

            if (loop is not null)
            {
                try { await loop.WaitAsync(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) { /* beklenen */ }
                catch (TimeoutException) { _logger.LogWarning("Watch loop did not stop within 10s."); }
                catch (Exception ex) { _logger.LogError(ex, "Watch loop faulted on shutdown."); }
            }

            cts.Dispose();
            _logs.Log(LogLevel.Info, "Watcher", "Stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var kvp in _accounts.ToArray())
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await CheckAccountAsync(kvp.Value, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Tek hesabın hatası tüm döngüyü öldürmesin
                    _logs.Log(LogLevel.Error, "Watcher",
                        $"Check failed: {ex.GetType().Name}", kvp.Key, ex.Message);
                }
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task CheckAccountAsync(Account account, CancellationToken ct)
    {
        // 1. Cookie sağlığı — süresi geldiyse yenile
        if (_refresher.NeedsRefresh(account))
        {
            string? fresh = await _refresher.RefreshAsync(account, ct);

            if (fresh is null)
            {
                var dead = account with
                {
                    State = AccountState.Error,
                    LastChecked = DateTime.UtcNow,
                    LastError = "Cookie expired — re-add account"
                };
                _accounts[account.Username] = dead;
                NotifyStateChange(dead);
                return;
            }

            if (!string.Equals(fresh, account.Cookie, StringComparison.Ordinal))
            {
                account = account with { Cookie = fresh };
                _accounts[account.Username] = account;
            }
        }

        // 2. Process durumu
        bool running = await _launcher.IsRunningAsync(account.Username, ct);
        var previous = account.State;

        AccountState newState = running ? AccountState.InGame : AccountState.Offline;

        if (newState == previous)
        {
            // Durum aynı ama timestamp tazele
            var tick = account with { LastChecked = DateTime.UtcNow };
            _accounts[account.Username] = tick;
            return;
        }

        var updated = account with { State = newState, LastChecked = DateTime.UtcNow };
        _accounts[account.Username] = updated;
        NotifyStateChange(updated);

        // 3. Oyundayken düştüyse → yeniden katıl
        if (previous == AccountState.InGame && newState == AccountState.Offline && account.HasTarget)
        {
            _logs.Log(LogLevel.Warning, "Watcher", "Disconnect detected, scheduling rejoin", account.Username);

            var rejoining = updated with { State = AccountState.Rejoining };
            _accounts[account.Username] = rejoining;
            NotifyStateChange(rejoining);

            _ = SafeRejoinAsync(rejoining, ct);
        }
    }

    private async Task SafeRejoinAsync(Account account, CancellationToken ct)
    {
        try
        {
            await _rejoin.HandleDisconnectAsync(account, ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logs.Log(LogLevel.Error, "Watcher",
                $"Rejoin failed: {ex.Message}", account.Username, ex.ToString());

            if (_accounts.TryGetValue(account.Username, out var cur))
            {
                var errored = cur with { State = AccountState.Error, LastError = ex.Message };
                _accounts[account.Username] = errored;
                NotifyStateChange(errored);
            }
        }
    }

    private void NotifyStateChange(Account account)
    {
        try { AccountStateChanged?.Invoke(account); }
        catch (Exception ex) { _logger.LogError(ex, "State change handler threw"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopWatchingAsync(); } catch { /* best effort */ }
        _lifecycleGate.Dispose();
    }
}