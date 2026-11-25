// src/Core/Services/ILogService.cs
namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface ILogService
{
    IReadOnlyList<LogEntry> Entries { get; }
    event Action<LogEntry>? LogAdded;
    
    void Log(LogLevel level, string source, string message, string? username = null, string? details = null);
    void Clear();
    IEnumerable<LogEntry> Filter(string? username = null, LogLevel? minLevel = null, string? search = null);
    Task ExportAsync(string filePath, CancellationToken ct = default);
}