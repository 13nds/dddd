// src/Panel/Services/TrayService.cs
namespace RobloxAutoManager.Panel.Services;

using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Action _onShow;
    private readonly Action _onExit;
    private bool _disposed;

    public TrayService(Action onShow, Action? onExit = null)
    {
        _onShow = onShow;
        _onExit = onExit ?? (() => Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RobloxAutoManager",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Panel", null, (_, _) => SafeInvoke(_onShow));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => SafeInvoke(_onExit));

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => SafeInvoke(_onShow);
    }

    public void SetTooltip(string text)
    {
        if (_disposed) return;
        // NotifyIcon.Text 63 karakterle sınırlı
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_disposed) return;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(3000);
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            var app = Application.Current;
            if (app?.Dispatcher.CheckAccess() == false)
                app.Dispatcher.Invoke(action);
            else
                action();
        }
        catch { /* shutdown sırasında dispatcher ölmüş olabilir */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}