using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly AgentStatusService _statusService;
    private readonly EventStoreReader _eventStoreReader;

    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private bool _isPaired;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _protectionStatus = "Connecting...";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private double _batteryPercent;
    [ObservableProperty] private string _batteryStatus = "Unknown";
    [ObservableProperty] private int _pendingEvents;
    [ObservableProperty] private int _totalEvents;
    [ObservableProperty] private string _hostname = "";
    [ObservableProperty] private string _osVersion = "";

    public ObservableCollection<EventRecord> RecentEvents { get; } = new();

    public DashboardViewModel(AgentStatusService statusService, EventStoreReader eventStoreReader)
    {
        _statusService = statusService;
        _eventStoreReader = eventStoreReader;
        _statusService.StatusChanged += OnStatusChanged;
    }

    private async void OnStatusChanged(object? sender, AgentStatus status)
    {
        void Apply()
        {
            IsServiceRunning = status.IsRunning;
            IsPaired = status.IsPaired;
            IsOnline = status.IsOnline;
            ProtectionStatus = status.ProtectionStatus;
            CpuPercent = status.CpuPercent ?? 0;
            MemoryPercent = status.MemoryPercent ?? 0;
            DiskPercent = status.DiskPercent ?? 0;
            BatteryPercent = status.BatteryPercent ?? 0;
            BatteryStatus = status.BatteryStatus ?? "Unknown";
            PendingEvents = status.PendingEvents;
            TotalEvents = status.TotalEvents;
            Hostname = status.Hostname ?? Environment.MachineName;
            OsVersion = status.OsVersion ?? Environment.OSVersion.VersionString;
        }

        if (App.Current?.Dispatcher is { } dispatcher)
            await dispatcher.InvokeAsync(Apply);
        else
            Apply();

        await LoadRecentEventsAsync();
    }

    public async Task LoadRecentEventsAsync()
    {
        var events = await _eventStoreReader.GetRecentEventsAsync(10);

        void Apply()
        {
            RecentEvents.Clear();
            foreach (var e in events)
                RecentEvents.Add(e);
        }

        if (App.Current?.Dispatcher is { } dispatcher)
            await dispatcher.InvokeAsync(Apply);
        else
            Apply();
    }
}
