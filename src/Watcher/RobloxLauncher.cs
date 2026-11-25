// src/Watcher/RobloxLauncher.cs (UPDATED — uses MultiInstanceManager)
namespace RobloxAutoManager.Watcher;

using System.Diagnostics;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;
using Microsoft.Extensions.Logging;

public sealed class RobloxLauncher : IRobloxLauncher
{
    private readonly ILogger<RobloxLauncher> _logger;
    private readonly MultiInstanceManager _instanceMgr;
    private const string RobloxProcessName = "RobloxPlayerBeta";
    private static readonly SemaphoreSlim _launchLock = new(1, 1);

    public RobloxLauncher(ILogger<RobloxLauncher> logger, MultiInstanceManager instanceMgr)
    {
        _logger = logger;
        _instanceMgr = instanceMgr;
    }

    public async Task<bool> LaunchAsync(Account account, CancellationToken ct = default)
    {
        await _launchLock.WaitAsync(ct);
        try
        {
            if (_instanceMgr.IsRunning(account.Username))
            {
                _logger.LogDebug("{User} already running in isolated job. Skipping.", account.Username);
                return true;
            }

            string uri = account.TargetServer.GetLaunchUri();
            _logger.LogInformation("Launching {User} → {Uri}", account.Username, uri);

            // Cookie'yi geçici registry key'e yaz (isolation için unique key)
            string regKey = $@"HKEY_CURRENT_USER\SOFTWARE\RobloxAutoManager\{account.Username}";
            Microsoft.Win32.Registry.SetValue(regKey, ".ROBLOSECURITY", account.Cookie);

            // URI scheme ile başlat
            var psi = new ProcessStartInfo(uri)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                _logger.LogError("Failed to start process for {User}", account.Username);
                return false;
            }

            // Gerçek RobloxPlayerBeta process'ini bul ve izole et
            Process? robloxProc = await WaitForRobloxProcessAsync(TimeSpan.FromSeconds(30), ct);
            if (robloxProc != null)
            {
                _instanceMgr.RegisterProcess(account.Username, robloxProc);
            }

            return robloxProc != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch failed for {User}", account.Username);
            return false;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    public Task<bool> IsRunningAsync(string username, CancellationToken ct = default)
    {
        return Task.FromResult(_instanceMgr.IsRunning(username));
    }

    public Task KillAsync(string username, CancellationToken ct = default)
    {
        _instanceMgr.KillAccount(username);
        return Task.CompletedTask;
    }

    private static async Task<Process?> WaitForRobloxProcessAsync(TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var procs = Process.GetProcessesByName(RobloxProcessName);
            if (procs.Length > 0)
            {
                // En yeni başlayan process'i al (bizimki)
                var newest = procs.OrderByDescending(p => p.StartTime).First();
                foreach (var p in procs.Where(p => p.Id != newest.Id)) p.Dispose();
                return newest;
            }
            await Task.Delay(500, ct);
        }
        return null;
    }
}