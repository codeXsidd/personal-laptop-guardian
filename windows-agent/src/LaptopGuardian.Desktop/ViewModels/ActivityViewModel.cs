using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class ActivityViewModel : ObservableObject
{
    private readonly EventStoreReader _eventStoreReader;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _selectedFilter;

    public ObservableCollection<EventRecord> Events { get; } = new();
    public ObservableCollection<string> FilterOptions { get; } = new()
    {
        "All",
        "Login / Logout",
        "Process",
        "USB",
        "Network",
        "File Access",
        "System",
    };

    public ActivityViewModel(EventStoreReader eventStoreReader)
    {
        _eventStoreReader = eventStoreReader;
        SelectedFilter = "All";
    }

    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        IsLoading = true;
        var events = await _eventStoreReader.GetRecentEventsAsync(200);

        if (SelectedFilter is not null and not "All")
        {
            events = events.Where(e => MatchesFilter(e, SelectedFilter)).ToList();
        }

        void Apply()
        {
            Events.Clear();
            foreach (var e in events)
                Events.Add(e);
            IsLoading = false;
        }

        if (App.Current?.Dispatcher is { } dispatcher)
            await dispatcher.InvokeAsync(Apply);
        else
            Apply();
    }

    partial void OnSelectedFilterChanged(string? value)
    {
        _ = LoadEventsAsync();
    }

    private static bool MatchesFilter(EventRecord e, string filter)
    {
        return filter switch
        {
            "Login / Logout" => e.EventType.Contains("login") || e.EventType.Contains("logout")
                                || e.EventType.Contains("lock") || e.EventType.Contains("unlock"),
            "Process" => e.EventType.Contains("process"),
            "USB" => e.EventType.Contains("usb"),
            "Network" => e.EventType.Contains("network"),
            "File Access" => e.EventType.Contains("file"),
            "System" => e.EventType.Contains("system") || e.EventType.Contains("agent")
                         || e.EventType.Contains("eventlog") || e.EventType.Contains("metrics"),
            _ => true,
        };
    }
}
