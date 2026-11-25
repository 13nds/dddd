namespace RobloxAutoManager.Core.Services;

using RobloxAutoManager.Core.Models;

public interface ILicenseValidator
{
    Task<LicenseValidationResult> ValidateAsync(string key, CancellationToken ct = default);
    string GetCurrentHwid();
}

public sealed record LicenseValidationResult(
    bool Success,
    LicenseKey? License = null,
    string? ErrorMessage = null
);