// src/Panel/Views/LogViewer.xaml.cs
namespace RobloxAutoManager.Panel.Views;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public partial class LogViewer : UserControl
{
    private readonly ILogService _logService;
    private readonly ObservableCollection<LogEntry> _filteredLogs = new();

    public LogViewer(ILogService logService)
    {
        InitializeComponent();
        _logService = logService;
        LogList.ItemsSource = _filteredLogs;
        
        _logService.LogAdded += OnLogAdded;
        RefreshFilters();
        ApplyFilter();
    }

    private void OnLogAdded(LogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            // Auto-add user to filter dropdown
            if (!string.IsNullOrEmpty(entry.Username) && !UserFilter.Items.Cast<ComboBoxItem>().Any(i => i.Content?.ToString() == entry.Username))
            {
                UserFilter.Items.Add(new ComboBoxItem { Content = entry.Username });
            }
            
            ApplyFilter();
            CountLabel.Text = $"{_filteredLogs.Count} entries";
            
            // Auto-scroll to bottom
            if (_filteredLogs.Count > 0)
                LogList.ScrollIntoView(_filteredLogs[^1]);
        });
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string? search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text;
        LogLevel? minLevel = LevelFilter.SelectedIndex switch
        {
            1 => LogLevel.Info,
            2 => LogLevel.Warning,
            3 => LogLevel.Error,
            _ => null
        };
        string? user = (UserFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

        var filtered = _logService.Filter(user, minLevel, search);
        
        _filteredLogs.Clear();
        foreach (var entry in filtered)
            _filteredLogs.Add(entry);
        
        CountLabel.Text = $"{_filteredLogs.Count} entries";
    }

    private void RefreshFilters()
    {
        UserFilter.Items.Clear();
        UserFilter.Items.Add(new ComboBoxItem { Content = "All Users" });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON Files|*.json",
            FileName = $"roblox-logs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        
        if (dlg.ShowDialog() == true)
        {
            await _logService.ExportAsync(dlg.FileName);
            MessageBox.Show($"Exported to {dlg.FileName}", "Success");
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        _filteredLogs.Clear();
        CountLabel.Text = "0 entries";
    }
}