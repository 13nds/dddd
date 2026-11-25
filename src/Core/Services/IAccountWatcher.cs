namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface IAccountWatcher : IAsyncDisposable
{
    IReadOnlyList<Account> Accounts { get; }
    event Action<Account>? AccountStateChanged;
    
    Task AddAccountAsync(Account account, CancellationToken ct = default);
    Task RemoveAccountAsync(string username, CancellationToken ct = default);
    Task UpdateServerConfigAsync(string username, ServerConfig config, CancellationToken ct = default);
    Task StartWatchingAsync(CancellationToken ct = default);
    Task StopWatchingAsync(CancellationToken ct = default);
}