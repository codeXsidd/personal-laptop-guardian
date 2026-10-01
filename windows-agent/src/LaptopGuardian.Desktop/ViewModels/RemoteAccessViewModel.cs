using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class RemoteAccessViewModel : ObservableObject
{
    private readonly RemoteAccessService _service;

    [ObservableProperty] private string _status = "Waiting for remote access requests...";
    [ObservableProperty] private bool _hasActiveSession;
    [ObservableProperty] private bool _hasPendingRequest;
    [ObservableProperty] private string? _pendingSessionId;
    [ObservableProperty] private bool _isProcessing;

    public RemoteAccessViewModel(RemoteAccessService service)
    {
        _service = service;

        _service.SessionRequested += OnSessionRequested;
        _service.SessionEnded += OnSessionEnded;
        _service.StatusChanged += OnStatusChanged;
        _service.Error += OnError;
    }

    public async Task InitializeAsync() => await _service.InitializeAsync();

    private void OnSessionRequested(object? sender, RemoteSessionRequest request)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            PendingSessionId = request.SessionId;
            HasPendingRequest = true;
            Status = "Remote access requested from your phone. Approve?";
        });
    }

    private void OnSessionEnded(object? sender, string reason)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            HasActiveSession = false;
            HasPendingRequest = false;
            PendingSessionId = null;
            Status = $"Session ended: {reason}";
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Status = status;
            HasActiveSession = _service.IsActive;
        });
    }

    private void OnError(object? sender, string error)
    {
        Application.Current?.Dispatcher.Invoke(() => Status = $"Error: {error}");
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (PendingSessionId == null) return;
        IsProcessing = true;
        HasPendingRequest = false;

        var sid = PendingSessionId;
        PendingSessionId = null;
        Status = "Approving session...";

        await _service.ApproveSessionAsync(sid);
        IsProcessing = false;
    }

    [RelayCommand]
    private async Task RejectAsync()
    {
        if (PendingSessionId == null) return;
        IsProcessing = true;

        await _service.RejectSessionAsync(PendingSessionId);
        HasPendingRequest = false;
        PendingSessionId = null;
        Status = "Session rejected";
        IsProcessing = false;
    }

    [RelayCommand]
    private async Task EndSessionAsync()
    {
        IsProcessing = true;
        await _service.EndSessionAsync("ended_by_desktop_user");
        IsProcessing = false;
    }
}
