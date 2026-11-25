// src/Panel/Services/TrayService.cs
namespace RobloxAutoManager.Panel.Services;

using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Action _onShow;

    public TrayService(Action onShow)
    {
        _onShow = onShow;
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // Prod'da .ico dosyası kullan
            Text = "RobloxAutoManager",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Panel", null, (_, _) => _onShow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => _onShow();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}