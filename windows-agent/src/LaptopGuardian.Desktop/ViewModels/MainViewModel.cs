using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PinService _pinService;
    private readonly DispatcherTimer _autoLockTimer;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _selectedNav = "Dashboard";

    [ObservableProperty]
    private bool _isAppLocked;

    [ObservableProperty]
    private int _autoLockMinutes = 5;

    public DashboardViewModel Dashboard { get; }
    public PairPhoneViewModel PairPhone { get; }
    public ActivityViewModel Activity { get; }
    public DeviceViewModel Device { get; }
    public SettingsViewModel Settings { get; }
    public PinLockViewModel PinLock { get; }
    public RemoteAccessViewModel RemoteAccess { get; }

    public MainViewModel(
        DashboardViewModel dashboard,
        PairPhoneViewModel pairPhone,
        ActivityViewModel activity,
        DeviceViewModel device,
        SettingsViewModel settings,
        PinService pinService,
        PinLockViewModel pinLock,
        RemoteAccessViewModel remoteAccess)
    {
        Dashboard = dashboard;
        PairPhone = pairPhone;
        Activity = activity;
        Device = device;
        Settings = settings;
        PinLock = pinLock;
        RemoteAccess = remoteAccess;
        _pinService = pinService;

        CurrentView = Dashboard;

        RemoteAccess.NavigationRequested += (_, _) =>
        {
            Navigate("RemoteAccess");
            // Bring window to front
            if (System.Windows.Application.Current?.MainWindow is { } win)
            {
                if (win.WindowState == System.Windows.WindowState.Minimized)
                    win.WindowState = System.Windows.WindowState.Normal;
                win.Activate();
            }
        };

        // Auto-lock timer
        _autoLockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(AutoLockMinutes)
        };
        _autoLockTimer.Tick += OnAutoLockTimerTick;

        // Start timer if PIN is set
        if (pinService.HasPin)
        {
            _autoLockTimer.Start();
        }

        // Subscribe to unlock event
        PinLock.Unlocked += (s, e) =>
        {
            IsAppLocked = false;
            ResetAutoLockTimer();
        };
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedNav = page;
        CurrentView = page switch
        {
            "Dashboard" => Dashboard,
            "PairPhone" => PairPhone,
            "Activity" => Activity,
            "Device" => Device,
            "Settings" => Settings,
            "RemoteAccess" => RemoteAccess,
            _ => Dashboard,
        };

        ResetAutoLockTimer();
    }

    [RelayCommand(CanExecute = nameof(CanLock))]
    public void Lock()
    {
        if (!_pinService.HasPin)
            return;

        IsAppLocked = true;
        PinLock.Lock();
        _autoLockTimer.Stop();
    }

    private bool CanLock() => _pinService.HasPin && !IsAppLocked;

    public void RefreshLockState()
    {
        // Called when PIN is set/removed
        if (_pinService.HasPin)
        {
            _autoLockTimer.Start();
        }
        else
        {
            _autoLockTimer.Stop();
            IsAppLocked = false;
        }
        LockCommand.NotifyCanExecuteChanged();
    }

    private void ResetAutoLockTimer()
    {
        if (_pinService.HasPin && !IsAppLocked)
        {
            _autoLockTimer.Stop();
            _autoLockTimer.Start();
        }
    }

    private void OnAutoLockTimerTick(object? sender, EventArgs e)
    {
        Lock();
    }

    partial void OnAutoLockMinutesChanged(int value)
    {
        _autoLockTimer.Interval = TimeSpan.FromMinutes(value);
        ResetAutoLockTimer();
    }
}
