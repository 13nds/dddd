// src/Core/Models/Account.cs
namespace RobloxAutoManager.Core.Models;

public sealed record Account(
    string Username,
    string Cookie,
    ServerConfig TargetServer,
    AccountState State = AccountState.Offline,
    DateTime LastChecked = default,
    string? CurrentJobId = null,
    long UserId = 0,
    string? LastError = null,
    int RejoinAttempts = 0
)
{
    /// Uzunluk kontrolü YOK — Roblox cookie formatını önceden duyurmadan değiştirebiliyor.
    /// Gerçek doğrulama RobloxAuthClient.ValidateAsync ile yapılır.
    public bool HasCookie => !string.IsNullOrWhiteSpace(Cookie);

    public bool HasTarget => TargetServer.PlaceId > 0;

    public bool IsReady => HasCookie && HasTarget && UserId > 0;
}

public enum AccountState
{
    Offline,
    Launching,
    InGame,
    Rejoining,
    Disconnected,
    Error,
    LicenseExpired
}