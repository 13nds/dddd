namespace RobloxAutoManager.Core.Utils;

using System.Diagnostics;
using System.Runtime.InteropServices;

public static class ProcessHelper
{
    public static bool IsProcessRunning(string processName)
    {
        return Process.GetProcessesByName(processName).Length > 0;
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

    public static string GetHwid() => HardwareId.Value;
    {
        // Basit HWID: MachineGuid + CPU ID hash
        string machineId = Environment.MachineName;
        string userDomain = Environment.UserDomainName;
        string raw = $"{machineId}|{userDomain}|{RuntimeInformation.OSArchitecture}";
        
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32]; // İlk 32 hex char
    }
}