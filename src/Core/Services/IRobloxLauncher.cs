namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface IRobloxLauncher
{
    Task<bool> LaunchAsync(Account account, CancellationToken ct = default);
    Task<bool> IsRunningAsync(string username, CancellationToken ct = default);
    Task KillAsync(string username, CancellationToken ct = default);
}