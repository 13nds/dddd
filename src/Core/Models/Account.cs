namespace RobloxAutoManager.Core.Models;

public sealed record Account(
    string Username,
    string Cookie,           // .ROBLOSECURITY
    ServerConfig TargetServer,
    AccountState State = AccountState.Offline,
    DateTime LastChecked = default,
    string? CurrentJobId = null
)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Cookie) && Cookie.Length > 500;
}

public enum AccountState
{
    Offline,
    Launching,
    InGame,
    Rejoining,
    Error,
    LicenseExpired
}