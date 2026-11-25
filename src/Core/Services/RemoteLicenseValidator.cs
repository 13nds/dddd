// src/Core/Services/RemoteLicenseValidator.cs
namespace RobloxAutoManager.Core.Services;

using System.Net.Http.Json;
using System.Text.Json;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Utils;

public sealed class RemoteLicenseValidator : ILicenseValidator
{
    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly bool _allowOfflineGrace;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RemoteLicenseValidator(LicenseApiConfig? config = null)
    {
        config ??= LicenseApiConfig.FromEnvironment();
        _apiBase = config.BaseUrl.TrimEnd('/');
        _allowOfflineGrace = config.AllowOfflineGrace;

        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<LicenseValidationResult> ValidateAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new LicenseValidationResult(false, ErrorMessage: "Key cannot be empty.");

        string hwid = GetCurrentHwid();

        // API yapılandırılmamışsa: yerel geliştirme modu
        if (string.IsNullOrWhiteSpace(_apiBase))
        {
            if (!_allowOfflineGrace)
                return new LicenseValidationResult(false, ErrorMessage: "License API not configured.");

            var local = new LicenseKey(
                Key: key,
                Hwid: hwid,
                ExpiresAt: DateTime.UtcNow.AddDays(30),
                MaxAccounts: 100,
                IsActive: true);

            return new LicenseValidationResult(true, License: local);
        }

        try
        {
            var payload = new { Key = key, Hwid = hwid };
            using var response = await _http.PostAsJsonAsync($"{_apiBase}/validate", payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return new LicenseValidationResult(false,
                    ErrorMessage: string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)response.StatusCode}" : err);
            }

            var license = await response.Content.ReadFromJsonAsync<LicenseKey>(JsonOpts, ct);

            if (license is null)
                return new LicenseValidationResult(false, ErrorMessage: "Malformed license response.");

            return new LicenseValidationResult(true, License: license);
        }
        catch (Exception ex)
        {
            if (_allowOfflineGrace)
            {
                var grace = new LicenseKey(
                    Key: key,
                    Hwid: hwid,
                    ExpiresAt: DateTime.UtcNow.AddDays(7),
                    MaxAccounts: 25,
                    IsActive: true);

                return new LicenseValidationResult(true, License: grace);
            }

            return new LicenseValidationResult(false, ErrorMessage: $"Network error: {ex.Message}");
        }
    }

    public string GetCurrentHwid() => HardwareId.Value;
}

public sealed record LicenseApiConfig(string BaseUrl, bool AllowOfflineGrace)
{
    public static LicenseApiConfig FromEnvironment()
    {
        string url = Environment.GetEnvironmentVariable("RAM_LICENSE_API") ?? "";
        string graceRaw = Environment.GetEnvironmentVariable("RAM_OFFLINE_GRACE") ?? "true";
        bool grace = !graceRaw.Equals("false", StringComparison.OrdinalIgnoreCase) && graceRaw != "0";
        return new LicenseApiConfig(url, grace);
    }
}