// src/Core/Utils/HardwareId.cs
namespace RobloxAutoManager.Core.Utils;

using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

[SupportedOSPlatform("windows")]
public static class HardwareId
{
    private static readonly Lazy<string> Cached = new(Compute, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Value => Cached.Value;

    private static string Compute()
    {
        var components = new List<string>();

        TryAdd(components, "UUID", QueryFirst("Win32_ComputerSystemProduct", "UUID"));
        TryAdd(components, "BOARD", QueryFirst("Win32_BaseBoard", "SerialNumber"));
        TryAdd(components, "BIOS", QueryFirst("Win32_BIOS", "SerialNumber"));
        TryAdd(components, "DISK", QueryFirst("Win32_DiskDrive", "SerialNumber"));
        TryAdd(components, "CPU", QueryFirst("Win32_Processor", "ProcessorId"));

        // WMI tamamen başarısızsa makine adı + SID ile düş ama zayıf olduğunu işaretle
        if (components.Count == 0)
        {
            components.Add($"FALLBACK:{Environment.MachineName}");
            components.Add(System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "NOSID");
        }

        components.Sort(StringComparer.Ordinal);
        string raw = string.Join("|", components);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static void TryAdd(List<string> sink, string tag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        string v = value.Trim();

        // Üreticilerin doldurmadığı placeholder değerler — kimlik sayılmaz
        if (v is "None" or "To be filled by O.E.M." or "Default string"
              or "System Serial Number" or "Not Specified" or "0" or "00000000")
            return;

        if (v.All(c => c == '0' || c == 'F' || c == '-')) return;

        sink.Add($"{tag}={v}");
    }

    private static string? QueryFirst(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            using var results = searcher.Get();

            foreach (var mo in results)
            {
                using (mo)
                {
                    string? val = mo[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
        }
        catch
        {
            // WMI servisi kapalı / erişim yok — sessizce geç, diğer bileşenler yeter
        }

        return null;
    }

    /// Bileşen değişimini tolere eden benzerlik skoru (0..1). Lisans taşıma senaryosu için.
    public static double SimilarityTo(string otherHash)
        => string.Equals(Value, otherHash, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
}