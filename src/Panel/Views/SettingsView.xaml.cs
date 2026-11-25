// src/Panel/Views/SettingsView.xaml.cs
namespace RobloxAutoManager.Panel.Views;

using System.Windows;
using System.Windows.Controls;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public partial class SettingsView : UserControl
{
    private readonly IAccountWatcher _watcher;
    private readonly string _username;

    public SettingsView(IAccountWatcher watcher, string username)
    {
        InitializeComponent();
        _watcher = watcher;
        _username = username;
        LoadCurrentConfig();
    }

    private void LoadCurrentConfig()
    {
        var acc = _watcher.Accounts.FirstOrDefault(a => a.Username == _username);
        if (acc == null) return;

        PlaceIdBox.Text = acc.TargetServer.PlaceId.ToString();
        JobIdBox.Text = acc.TargetServer.JobId ?? "";
        PriorityCombo.SelectedIndex = (int)acc.TargetServer.Priority;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(PlaceIdBox.Text.Trim(), out long placeId) || placeId <= 0)
        {
            MessageBox.Show("Invalid Place ID", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? jobId = string.IsNullOrWhiteSpace(JobIdBox.Text) ? null : JobIdBox.Text.Trim();
        var priority = (RejoinPriority)PriorityCombo.SelectedIndex;

        var config = new ServerConfig(placeId, jobId, Priority: priority);
        await _watcher.UpdateServerConfigAsync(_username, config);

        MessageBox.Show("Configuration saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}