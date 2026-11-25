// src/Core/Services/ICookieRefresher.cs
namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface ICookieRefresher
{
    Task<string?> RefreshAsync(Account account, CancellationToken ct = default);
    bool NeedsRefresh(Account account);
}