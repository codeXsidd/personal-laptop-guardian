using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.Views;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class PairPhoneViewModel : ObservableObject
{
    private readonly AgentStatusService _statusService;
    private System.Timers.Timer? _countdownTimer;

    [ObservableProperty] private bool _isPaired;
    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private string _countdownText = "";
    [ObservableProperty] private DateTimeOffset? _pairedAt;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _codeExpired;
    [ObservableProperty] private bool _isUnpairing;

    private DateTimeOffset? _expiresAt;

    public PairPhoneViewModel(AgentStatusService statusService)
    {
        _statusService = statusService;
        _statusService.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, AgentStatus status)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsServiceRunning = status.IsRunning;
            IsPaired = status.IsPaired;
            PairedAt = status.PairedAt;

            if (!IsPaired && !string.IsNullOrEmpty(status.PairingCode))
            {
                PairingCode = status.PairingCode;
                _expiresAt = status.PairingCodeExpiresAt;
                StartCountdown();
            }
        });
    }

    [RelayCommand]
    private async Task GenerateNewCodeAsync()
    {
        IsGenerating = true;
        ErrorMessage = null;
        CodeExpired = false;

        var (success, code, expiresAt, error) = await _statusService.RefreshPairingCodeAsync();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsGenerating = false;
            if (success && code is not null)
            {
                PairingCode = code;
                _expiresAt = expiresAt;
                StartCountdown();
            }
            else
            {
                ErrorMessage = error ?? "Failed to generate code";
            }
        });
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (!string.IsNullOrEmpty(PairingCode))
            Clipboard.SetText(PairingCode);
    }

    [RelayCommand]
    private void ShowQrCode()
    {
        if (string.IsNullOrEmpty(PairingCode)) return;
        var qrImage = QrCodeGenerator.GenerateQrCode(PairingCode);
        var dialog = new QrCodeDialog(qrImage, PairingCode)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
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

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsUnpairing = false;
            if (!success)
            {
                ErrorMessage = error ?? "Failed to unpair device";
            }
        });
    }

    private void StartCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();

        _countdownTimer = new System.Timers.Timer(1000);
        _countdownTimer.Elapsed += (_, _) => UpdateCountdown();
        _countdownTimer.Start();
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (_expiresAt is null) return;

        var remaining = _expiresAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _countdownTimer?.Stop();
            Application.Current.Dispatcher.Invoke(() =>
            {
                CountdownText = "Expired";
                CodeExpired = true;
            });
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            CountdownText = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
            CodeExpired = false;
        });
    }
}
