// src/Core/Services/ITokenStore.cs
namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface ITokenStore
{
    Task SaveAsync(OAuthToken token, CancellationToken ct = default);
    Task<OAuthToken?> LoadAsync(string username, CancellationToken ct = default);
    Task DeleteAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default);
}