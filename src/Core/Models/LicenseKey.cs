namespace RobloxAutoManager.Core.Models;

public sealed record LicenseKey(
    string Key,
    string Hwid,
    DateTime ExpiresAt,
    int MaxAccounts,
    bool IsActive = true
)
{
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsValidFor(string hwid) => IsActive && !IsExpired && Hwid == hwid;
}