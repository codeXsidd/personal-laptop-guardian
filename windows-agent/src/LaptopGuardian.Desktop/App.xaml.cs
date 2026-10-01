using System.Windows;
using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;

namespace LaptopGuardian.Desktop;

public partial class App : Application
{
    private AgentStatusService? _statusService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _statusService = new AgentStatusService();
        var eventStoreReader = new EventStoreReader();
        var pinService = new PinService();

        var remoteAccessService = new RemoteAccessService();
        var dashboard = new DashboardViewModel(_statusService, eventStoreReader);
        var pairPhone = new PairPhoneViewModel(_statusService);
        var activity = new ActivityViewModel(eventStoreReader);
        var device = new DeviceViewModel(_statusService);
        var pinSetup = new PinSetupViewModel(pinService);
        var settings = new SettingsViewModel(pinSetup);
        var remoteAccess = new RemoteAccessViewModel(remoteAccessService);

        var pinLock = new PinLockViewModel(pinService);
        var mainVm = new MainViewModel(dashboard, pairPhone, activity, device, settings, pinService, pinLock, remoteAccess);

        // Wire up PIN change notifications
        pinSetup.PinChanged += (s, e) =>
        {
            mainVm.RefreshLockState();
            pinSetup.Refresh();
        };

        var mainWindow = new MainWindow { DataContext = mainVm };
        var startMinimized = e.Args.Contains("--minimized");
        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
        }
        mainWindow.Show();

        _statusService.Start();
        _ = remoteAccess.InitializeAsync();

        // Lock on startup if PIN is set
        if (pinService.HasPin)
        {
            mainVm.Lock();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _statusService?.Dispose();
        base.OnExit(e);
    }
}
