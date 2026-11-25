// src/Api/Program.cs — Minimal API License Server
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RobloxAutoManager.Core.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Basit in-memory store (prod'da DB kullan)
var licenses = new Dictionary<string, LicenseKey>(StringComparer.OrdinalIgnoreCase)
{
    ["TEST-KEY-001"] = new LicenseKey(
        Key: "TEST-KEY-001",
        Hwid: "ANY",  // Test için HWID bypass
        ExpiresAt: DateTime.UtcNow.AddDays(30),
        MaxAccounts: 50,
        IsActive: true
    )
};

app.MapPost("/api/license/validate", (ValidateRequest req) =>
{
    if (!licenses.TryGetValue(req.Key, out var license))
        return Results.BadRequest("Invalid license key.");

    if (!license.IsActive)
        return Results.BadRequest("License is disabled.");

    if (license.IsExpired)
        return Results.BadRequest("License has expired.");

    if (license.Hwid != "ANY" && license.Hwid != req.Hwid)
        return Results.Forbid();

    return Results.Ok(license);
});

app.Run();

record ValidateRequest(string Key, string Hwid);