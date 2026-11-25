// src/Core/Models/OAuthToken.cs
namespace RobloxAutoManager.Core.Models;

public sealed record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTime ExpiresAt,
    string Username,
    string UserId
)
{
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5); // 5min buffer
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken);
}