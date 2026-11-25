// src/Core/Models/LogEntry.cs
namespace RobloxAutoManager.Core.Models;

public sealed record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? Username = null,
    string? Details = null
)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}