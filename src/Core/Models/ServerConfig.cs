namespace RobloxAutoManager.Core.Models;

public sealed record ServerConfig(
    long PlaceId,
    string? JobId = null,        // Specific server instance (optional)
    string? CustomUrl = null,    // Private/VIP server link (optional)
    RejoinPriority Priority = RejoinPriority.JobIdFirst
)
{
    public string GetLaunchUri() => Priority switch
    {
        RejoinPriority.JobIdFirst when !string.IsNullOrWhiteSpace(JobId) 
            => $"roblox://placeId={PlaceId}&gameInstanceId={JobId}",
        RejoinPriority.CustomUrlFirst when !string.IsNullOrWhiteSpace(CustomUrl) 
            => CustomUrl,
        _ => $"roblox://placeId={PlaceId}"
    };
}

public enum RejoinPriority
{
    JobIdFirst,      // Try specific server → fallback to place
    CustomUrlFirst,  // Try VIP/private → fallback to place
    PlaceOnly        // Always join via placeId (matchmaking)
}