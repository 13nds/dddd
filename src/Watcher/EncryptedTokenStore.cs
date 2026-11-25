// src/Watcher/EncryptedTokenStore.cs
namespace RobloxAutoManager.Watcher;

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public sealed class EncryptedTokenStore : ITokenStore
{
    private readonly string _storePath;
    private readonly byte[] _entropy;

    public EncryptedTokenStore()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storePath = Path.Combine(appData, "RobloxAutoManager", "tokens");
        Directory.CreateDirectory(_storePath);
        
        // Machine-specific entropy for DPAPI
        _entropy = "RobloxAutoManager_v2_TokenStore"u8.ToArray();
    }

    public async Task SaveAsync(OAuthToken token, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(token);
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        // DPAPI encrypt — user + machine bound
        byte[] encrypted = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser);
        
        string filePath = GetFilePath(token.Username);
        await File.WriteAllBytesAsync(filePath, encrypted, ct);
    }

    public async Task<OAuthToken?> LoadAsync(string username, CancellationToken ct = default)
    {
        string filePath = GetFilePath(username);
        if (!File.Exists(filePath)) return null;

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(filePath, ct);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            string json = System.Text.Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<OAuthToken>(json);
        }
        catch
        {
            // Corrupted or tampered — delete and return null
            File.Delete(filePath);
            return null;
        }
    }

    public Task DeleteAsync(string username, CancellationToken ct = default)
    {
        string filePath = GetFilePath(username);
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetAllUsernamesAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_storePath, "*.tok");
        var usernames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
        return Task.FromResult<IReadOnlyList<string>>(usernames);
    }

    private string GetFilePath(string username)
    {
        // Sanitize username for filesystem
        string safe = new string(username.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return Path.Combine(_storePath, $"{safe}.tok");
    }
}