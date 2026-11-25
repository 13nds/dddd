// src/Core/Utils/ProcessHelper.cs
namespace RobloxAutoManager.Core.Utils;

using System.Diagnostics;

public static class ProcessHelper
{
    public static bool IsProcessRunning(string processName)
    {
        var procs = Process.GetProcessesByName(processName);
        try { return procs.Length > 0; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    public static int CountProcesses(string processName)
    {
        var procs = Process.GetProcessesByName(processName);
        try { return procs.Length; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    public static void KillProcessTree(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try { proc.Kill(entireProcessTree: true); }
            catch { /* already dead or access denied */ }
            finally { proc.Dispose(); }
        }
    }

    public static void KillByPid(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
        }
        catch { /* gone */ }
    }

    public static string GetHwid() => HardwareId.Value;
}