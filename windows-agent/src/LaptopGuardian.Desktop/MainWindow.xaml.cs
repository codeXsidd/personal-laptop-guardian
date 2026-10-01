using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace LaptopGuardian.Desktop;

public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Laptop Guardian",
            Visible = false,
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Laptop Guardian", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _forceClose = true;
            Close();
        });
        _trayIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon is not null)
            _trayIcon.Visible = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose && ShouldMinimizeToTray())
        {
            e.Cancel = true;
            Hide();
            if (_trayIcon is not null)
                _trayIcon.Visible = true;
            return;
        }

        _trayIcon?.Dispose();
        base.OnClosing(e);
    }

    private static bool ShouldMinimizeToTray()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaptopGuardian", "desktop-settings.json");
            if (!File.Exists(path)) return true;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("MinimizeToTray", out var prop))
                return prop.GetBoolean();
        }
        catch { }
        return true;
    }
}
