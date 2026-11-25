// src/Core/Services/RemoteLicenseValidator.cs
namespace RobloxAutoManager.Core.Services;

using System.Net.Http.Json;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Utils;

public sealed class RemoteLicenseValidator : ILicenseValidator
{
    private const string ApiBase = "https://your-api-domain.com/api/license";

    public async Task<LicenseValidationResult> ValidateAsync(string key, CancellationToken ct = default)
    {
        try
        {
            string hwid = GetCurrentHwid();
            var payload = new { Key = key, Hwid = hwid };

            var response = await RobloxHttpClient.Instance.PostAsJsonAsync($"{ApiBase}/validate", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return new LicenseValidationResult(false, ErrorMessage: err);
            }

            var license = await response.Content.ReadFromJsonAsync<LicenseKey>(ct);
            return new LicenseValidationResult(true, License: license);
        }
        catch (Exception ex)
        {
            return new LicenseValidationResult(false, ErrorMessage: $"Network error: {ex.Message}");
        }
    }

    public string GetCurrentHwid() => ProcessHelper.GetHwid();
}