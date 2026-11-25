// src/Panel/MainWindow.xaml.cs
namespace RobloxAutoManager.Panel;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Panel.ViewModels;
using RobloxAutoManager.Panel.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAccountWatcher _watcher;
    private readonly ILogService _logs;
    private readonly Func<string, SettingsView> _settingsFactory;
    private readonly LogViewer _logViewer;
    private readonly RateLimitMonitor _rateMonitor;

    private bool _forceClose;

    public MainWindow(
        MainViewModel viewModel,
        IAccountWatcher watcher,
        ILogService logs,
        Func<string, SettingsView> settingsFactory,
        LogViewer logViewer,
        RateLimitMonitor rateMonitor)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _watcher = watcher;
        _logs = logs;
        _settingsFactory = settingsFactory;
        _logViewer = logViewer;
        _rateMonitor = rateMonitor;

        DataContext = _viewModel;

        BuildTabs();
        WireSelection();
        WireCommands();

        Loaded += OnLoaded;
    }

    /// Tray "Exit" veya gerçek kapatma isteği. X tuşu buna dokunmaz.
    public void RequestForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void BuildTabs()
    {
        if (LogsTab != null) LogsTab.Content = _logViewer;
        if (QuotaTab != null) QuotaTab.Content = _rateMonitor;
    }

    private void WireSelection()
    {
        if (AccountGrid == null) return;

        AccountGrid.SelectionChanged += (_, _) =>
        {
            if (AccountGrid.SelectedItem is not Account account)
            {
                if (SettingsHost != null) SettingsHost.Content = BuildHint(null);
                return;
            }

            OpenSettingsFor(account.Username);
        };
    }

    private void WireCommands()
    {
        _viewModel.RequestOpenSettings += OpenSettingsFor;
        _viewModel.RequestForceQuit += RequestForceClose;
    }

    private void OpenSettingsFor(string username)
    {
        if (SettingsHost == null) return;

        try
        {
            SettingsHost.Content = _settingsFactory(username);

            if (MainTabs != null && SettingsTab != null)
                MainTabs.SelectedItem = SettingsTab;

            _logs.Log(LogLevel.Debug, "Panel", $"Settings opened", username);
        }
        catch (Exception ex)
        {
            _logs.Log(LogLevel.Error, "Panel",
                $"Failed to open settings: {ex.Message}", username, ex.ToString());
            SettingsHost.Content = BuildHint("Settings could not be loaded.");
        }
    }

    private static TextBlock BuildHint(string? message) => new()
    {
        Text = message ?? "Select an account from the Accounts tab to edit its server configuration.",
        Foreground = System.Windows.Media.Brushes.Gray,
        FontSize = 13,
        Margin = new Thickness(20),
        TextWrapping = TextWrapping.Wrap
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var account in _watcher.Accounts)
        {
            if (!_viewModel.Accounts.Any(a =>
                    a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase)))
                _viewModel.Accounts.Add(account);
        }

        _logs.Log(LogLevel.Info, "Panel",
            $"Window loaded. {_viewModel.Accounts.Count} accounts in view.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forceClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
        _logs.Log(LogLevel.Debug, "Panel", "Window hidden to tray.");
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized && !_forceClose)
        {
            Hide();
            _logs.Log(LogLevel.Debug, "Panel", "Minimized to tray.");
        }
    }
}