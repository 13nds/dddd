// src/Panel/App.xaml.cs
namespace RobloxAutoManager.Panel;

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Core.Utils;
using RobloxAutoManager.Panel.Auth;
using RobloxAutoManager.Panel.Services;
using RobloxAutoManager.Panel.ViewModels;
using RobloxAutoManager.Panel.Views;
using RobloxAutoManager.Watcher;
using RobloxAutoManager.Watcher.Auth;
using RobloxAutoManager.Watcher.Http;
using RobloxAutoManager.Watcher.RateLimiting;
using AppLogLevel = RobloxAutoManager.Core.Models.LogLevel;

public partial class App : Application
{
    private IHost? _host;
    private TrayService? _tray;
    private MainWindow? _main;
    private IDisposable? _rotationSubscription;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices(ConfigureServices)
                .Build();

            await _host.StartAsync();

            var logs = _host.Services.GetRequiredService<ILogService>();
            logs.Log(AppLogLevel.Info, "App", $"Host started. HWID={HardwareId.Value[..16]}…");

            WireCookieRotationPersistence(logs);

            if (!await AuthenticateAsync(logs))
            {
                Shutdown(0);
                return;
            }

            _main = _host.Services.GetRequiredService<MainWindow>();

            _tray = new TrayService(
                onShow: () =>
                {
                    _main.Show();
                    _main.WindowState = WindowState.Normal;
                    _main.Activate();
                },
                onExit: () =>
                {
                    _main.RequestForceClose();
                    Shutdown(0);
                });

            _main.Show();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            logs.Log(AppLogLevel.Info, "App", "Panel ready.");
        }
        catch (Exception ex)
        {
            TryLogFatal(ex, "Startup");

            MessageBox.Show(
                $"Startup failed:\n\n{ex.GetType().Name}: {ex.Message}",
                "RobloxAutoManager — Fatal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Observability ──────────────────────────────────────────
        services.AddSingleton<ILogService, LogService>();

        // ── Rate limiting ──────────────────────────────────────────
        services.AddSingleton<IRateLimiter, RobloxRateLimiter>();

        // ── HTTP pipeline ──────────────────────────────────────────
        services.AddSingleton<RateLimitAwareHandler>();

        services.AddSingleton(sp =>
        {
            var handler = sp.GetRequiredService<RateLimitAwareHandler>();
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        });

        services.AddSingleton(sp => new RobloxApiPipeline(
            http: sp.GetRequiredService<HttpClient>(),
            limiter: sp.GetRequiredService<IRateLimiter>(),
            logs: sp.GetRequiredService<ILogService>(),
            logger: sp.GetRequiredService<ILogger<RobloxApiPipeline>>()));

        // ── Cookie rotation + auth ─────────────────────────────────
        services.AddSingleton<CookieJar>();
        services.AddSingleton<RobloxAuthClient>();

        // ── Token storage + OAuth ──────────────────────────────────
        services.AddSingleton<ITokenStore, EncryptedTokenStore>();
        services.AddSingleton(LoadOAuthConfig());
        services.AddSingleton<OAuthManager>();
        services.AddSingleton<ICookieRefresher, CookieRefresher>();

        // ── License ────────────────────────────────────────────────
        services.AddSingleton(LicenseApiConfig.FromEnvironment());
        services.AddSingleton<ILicenseValidator, RemoteLicenseValidator>();

        // ── Process isolation + launch ─────────────────────────────
        services.AddSingleton<MultiInstanceManager>();
        services.AddSingleton<IRobloxLauncher, RobloxLauncher>();
        services.AddSingleton<RejoinStrategy>();
        services.AddSingleton<IAccountWatcher, AccountWatcher>();

        // ── UI: transient views ────────────────────────────────────
        services.AddTransient<AddAccountDialog>();
        services.AddTransient<LogViewer>();
        services.AddTransient<RateLimitMonitor>();

        // ── UI: factories ──────────────────────────────────────────
        services.AddSingleton<Func<AddAccountDialog>>(sp =>
            () => sp.GetRequiredService<AddAccountDialog>());

        services.AddSingleton<Func<string, SettingsView>>(sp =>
            username => new SettingsView(
                sp.GetRequiredService<IAccountWatcher>(),
                username));

        // ── UI: singletons ─────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void WireCookieRotationPersistence(ILogService logs)
    {
        var jar = _host!.Services.GetRequiredService<CookieJar>();

        void OnRotated(Core.Models.CookieRotation rotation)
        {
            logs.Log(AppLogLevel.Info, "Session",
                $"Cookie rotated at {rotation.At:HH:mm:ss}. Persistence pending (Batch 2).",
                rotation.Username);
        }

        jar.Rotated += OnRotated;
        _rotationSubscription = new ActionDisposable(() => jar.Rotated -= OnRotated);
    }

    private static OAuthClientConfig LoadOAuthConfig()
    {
        string clientId = Environment.GetEnvironmentVariable("RBX_CLIENT_ID") ?? "";
        string clientSecret = Environment.GetEnvironmentVariable("RBX_CLIENT_SECRET") ?? "";
        string redirect = Environment.GetEnvironmentVariable("RBX_REDIRECT_URI")
                          ?? "http://localhost:29871/callback";

        if (string.IsNullOrWhiteSpace(clientId))
        {
            string localFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RobloxAutoManager", "oauth.json");

            if (File.Exists(localFile))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localFile));
                    var root = doc.RootElement;

                    clientId = root.TryGetProperty("client_id", out var ci) ? ci.GetString() ?? "" : "";
                    clientSecret = root.TryGetProperty("client_secret", out var cs) ? cs.GetString() ?? "" : "";

                    if (root.TryGetProperty("redirect_uri", out var ru))
                        redirect = ru.GetString() ?? redirect;
                }
                catch { /* bozuk config startup'ı öldürmesin */ }
            }
        }

        return new OAuthClientConfig(clientId, clientSecret, redirect);
    }

    private async Task<bool> AuthenticateAsync(ILogService logs)
    {
        var validator = _host!.Services.GetRequiredService<ILicenseValidator>();
        var login = new LoginWindow(validator);

        bool? result = login.ShowDialog();

        if (result == true && login.IsAuthenticated)
        {
            logs.Log(AppLogLevel.Info, "Auth", "License validated.");
            return true;
        }

        logs.Log(AppLogLevel.Warning, "Auth", "License gate rejected. Shutting down.");
        return false;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLogFatal(e.Exception, "UI thread");
        e.Handled = true;

        MessageBox.Show(
            $"Unexpected UI error:\n\n{e.Exception.Message}",
            "RobloxAutoManager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            TryLogFatal(ex, "Background thread");
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        TryLogFatal(e.Exception, "Unobserved task");
        e.SetObserved();
    }

    private void TryLogFatal(Exception ex, string origin)
    {
        try
        {
            _host?.Services.GetService<ILogService>()?.Log(
                AppLogLevel.Fatal, "App",
                $"{origin}: {ex.GetType().Name} — {ex.Message}",
                details: ex.ToString());
        }
        catch { /* host ölmüş olabilir */ }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _rotationSubscription?.Dispose();

            if (_host != null)
            {
                var watcher = _host.Services.GetService<IAccountWatcher>();
                if (watcher != null)
                    await watcher.StopWatchingAsync();

                _host.Services.GetService<ILogService>()?
                    .Log(AppLogLevel.Info, "App", "Shutting down.");

                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch { /* best-effort teardown */ }
        finally
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? _action;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}