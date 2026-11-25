// src/Panel/Views/RateLimitMonitor.xaml.cs
namespace RobloxAutoManager.Panel.Views;

using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Threading;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;

public partial class RateLimitMonitor : UserControl
{
    private readonly IRateLimiter _limiter;
    private readonly ObservableCollection<RateLimitSnapshot> _rows = new();
    private readonly DispatcherTimer _timer;

    public RateLimitMonitor(IRateLimiter limiter)
    {
        InitializeComponent();
        _limiter = limiter;
        BucketGrid.ItemsSource = _rows;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snapshots = _limiter.GetAllSnapshots();
        _rows.Clear();
        foreach (var s in snapshots.Values.OrderBy(v => v.BucketKey))
            _rows.Add(s);
    }
}