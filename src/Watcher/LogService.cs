// src/Watcher/LogService.cs
namespace RobloxAutoManager.Watcher;

using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public sealed class LogService : ILogService
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly List<LogEntry> _snapshot = new();
    private readonly object _snapLock = new();
    private const int MaxEntries = 10000;

    public event Action<LogEntry>? LogAdded;

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_snapLock) { return _snapshot.ToList(); } }
    }

    public void Log(LogLevel level, string source, string message, string? username = null, string? details = null)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, source, message, username, details);
        
        _entries.Enqueue(entry);
        lock (_snapLock)
        {
            _snapshot.Add(entry);
            while (_snapshot.Count > MaxEntries)
                _snapshot.RemoveAt(0);
        }

        LogAdded?.Invoke(entry);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        lock (_snapLock) { _snapshot.Clear(); }
    }

    public IEnumerable<LogEntry> Filter(string? username = null, LogLevel? minLevel = null, string? search = null)
    {
        lock (_snapLock)
        {
            var query = _snapshot.AsEnumerable();
            
            if (!string.IsNullOrEmpty(username))
                query = query.Where(e => e.Username?.Equals(username, StringComparison.OrdinalIgnoreCase) == true);
            
            if (minLevel.HasValue)
                query = query.Where(e => e.Level >= minLevel.Value);
            
            if (!string.IsNullOrEmpty(search))
                query = query.Where(e => e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                         e.Details?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
            
            return query.ToList();
        }
    }

    public async Task ExportAsync(string filePath, CancellationToken ct = default)
    {
        var data = Entries;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, ct);
    }
}