// src/Watcher/MultiInstanceManager.cs
namespace RobloxAutoManager.Watcher;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;

public sealed class MultiInstanceManager
{
    private readonly ILogger<MultiInstanceManager> _logger;
    private readonly Dictionary<string, int> _accountPids = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // Job Object API — process group isolation
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly Dictionary<string, IntPtr> _jobs = new();

    public MultiInstanceManager(ILogger<MultiInstanceManager> logger)
    {
        _logger = logger;
    }

    public void RegisterProcess(string username, Process proc)
    {
        lock (_lock)
        {
            _accountPids[username] = proc.Id;

            // Her hesap için ayrı Job Object oluştur
            if (!_jobs.TryGetValue(username, out var job))
            {
                job = CreateJobObject(IntPtr.Zero, $"RobloxAutoMgr_{username}");
                _jobs[username] = job;
            }

            AssignProcessToJobObject(job, proc.Handle);
            _logger.LogInformation("Registered PID {Pid} for {User} in isolated job.", proc.Id, username);
        }
    }

    public bool IsRunning(string username)
    {
        lock (_lock)
        {
            if (!_accountPids.TryGetValue(username, out int pid))
                return false;

            try
            {
                using var proc = Process.GetProcessById(pid);
                return !proc.HasExited;
            }
            catch
            {
                _accountPids.Remove(username);
                return false;
            }
        }
    }

    public void KillAccount(string username)
    {
        lock (_lock)
        {
            if (_accountPids.TryGetValue(username, out int pid))
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                    _logger.LogInformation("Killed isolated process tree for {User} (PID {Pid})", username, pid);
                }
                catch { /* already dead */ }

                _accountPids.Remove(username);
            }

            if (_jobs.TryGetValue(username, out var job))
            {
                CloseHandle(job);
                _jobs.Remove(username);
            }
        }
    }

    public IReadOnlyDictionary<string, int> GetAllPids()
    {
        lock (_lock) { return new Dictionary<string, int>(_accountPids); }
    }
}