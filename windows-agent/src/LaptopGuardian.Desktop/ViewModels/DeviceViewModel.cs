using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class DeviceViewModel : ObservableObject
{
    private readonly AgentStatusService _statusService;

    [ObservableProperty] private string _deviceId = "";
    [ObservableProperty] private string _machineName = "";
    [ObservableProperty] private string _hostname = "";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private bool _isPaired;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private DateTimeOffset? _pairedAt;
    [ObservableProperty] private int _pendingEvents;
    [ObservableProperty] private int _totalEvents;
    [ObservableProperty] private bool _isUnpairing;
    [ObservableProperty] private string? _errorMessage;

    public DeviceViewModel(AgentStatusService statusService)
    {
        _statusService = statusService;
        _statusService.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, AgentStatus status)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            DeviceId = status.DeviceId ?? "";
            MachineName = status.MachineName ?? Environment.MachineName;
            Hostname = status.Hostname ?? Environment.MachineName;
            OsVersion = status.OsVersion ?? Environment.OSVersion.VersionString;
            IsPaired = status.IsPaired;
            IsOnline = status.IsOnline;
            IsServiceRunning = status.IsRunning;
            PairedAt = status.PairedAt;
            PendingEvents = status.PendingEvents;
            TotalEvents = status.TotalEvents;
        });
    }

    [RelayCommand]
    private async Task UnpairDeviceAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to unpair this device?\n\nYour phone will no longer receive notifications from this laptop. You can re-pair at any time.",
            "Unpair Device",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsUnpairing = true;
        ErrorMessage = null;

        var (success, error) = await _statusService.UnpairDeviceAsync();

        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            IsUnpairing = false;
            if (!success)
            {
                ErrorMessage = error ?? "Failed to unpair device";
            }
        });
    }
}
