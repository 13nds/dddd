// src/Watcher/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<IRobloxLauncher, RobloxLauncher>();
        services.AddSingleton<RejoinStrategy>();
        services.AddSingleton<IAccountWatcher, AccountWatcher>();
        
        // License validator: local or remote (swap implementation)
        services.AddSingleton<ILicenseValidator, RemoteLicenseValidator>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

// Panel başlatma (WPF App.xaml.cs'den çağrılacak)
// Bu satır sadece headless/test için. Gerçek UI App.xaml.cs'de host.Build() yapılır.
await host.StartAsync();
await host.WaitForShutdownAsync();