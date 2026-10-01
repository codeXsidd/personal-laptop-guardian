using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the PIN lock screen.
/// </summary>
public partial class PinLockViewModel : ObservableObject
{
    private readonly PinService _pinService;
    private readonly DispatcherTimer _lockoutTimer;

    [ObservableProperty]
    private string _pin = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isLocked = true;

    [ObservableProperty]
    private bool _isLockedOut;

    [ObservableProperty]
    private int _lockoutRemainingSeconds;

    public event EventHandler? Unlocked;

    public PinLockViewModel(PinService pinService)
    {
        _pinService = pinService;
        _lockoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _lockoutTimer.Tick += OnLockoutTimerTick;
    }

    [RelayCommand]
    private void AddDigit(string digit)
    {
        if (IsLockedOut || Pin.Length >= 8)
            return;

        Pin += digit;
        ErrorMessage = "";

        // Auto-submit on 8 digits
        if (Pin.Length == 8)
        {
            Unlock();
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (IsLockedOut || string.IsNullOrEmpty(Pin))
            return;

        Pin = Pin[..^1];
        ErrorMessage = "";
    }

    [RelayCommand]
    private void Clear()
    {
        Pin = "";
        ErrorMessage = "";
    }

    [RelayCommand]
    private void Unlock()
    {
        if (IsLockedOut)
        {
            ErrorMessage = $"Too many attempts. Try again in {LockoutRemainingSeconds}s";
            return;
        }

        if (string.IsNullOrEmpty(Pin))
        {
            ErrorMessage = "Please enter your PIN";
            return;
        }

        if (Pin.Length < 4)
        {
            ErrorMessage = "PIN must be at least 4 digits";
            return;
        }

        var isValid = _pinService.VerifyPin(Pin);

        if (isValid)
        {
            IsLocked = false;
            ErrorMessage = "";
            Pin = "";
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Check if locked out
            if (_pinService.IsLockedOut)
            {
                IsLockedOut = true;
                LockoutRemainingSeconds = _pinService.LockoutRemainingSeconds;
                ErrorMessage = $"Too many attempts. Try again in {LockoutRemainingSeconds}s";
                Pin = "";
                _lockoutTimer.Start();
            }
            else
            {
                var remaining = 5 - _pinService.FailedAttempts;
                ErrorMessage = remaining > 0
                    ? $"Incorrect PIN. {remaining} attempt{(remaining != 1 ? "s" : "")} remaining"
                    : "Incorrect PIN";
                Pin = "";
            }
        }
    }

    public void Lock()
    {
        IsLocked = true;
        Pin = "";
        ErrorMessage = "";
        IsLockedOut = _pinService.IsLockedOut;
        if (IsLockedOut)
        {
            LockoutRemainingSeconds = _pinService.LockoutRemainingSeconds;
            ErrorMessage = $"Too many attempts. Try again in {LockoutRemainingSeconds}s";
            _lockoutTimer.Start();
        }
    }

    private void OnLockoutTimerTick(object? sender, EventArgs e)
    {
        LockoutRemainingSeconds = _pinService.LockoutRemainingSeconds;

        if (LockoutRemainingSeconds <= 0)
        {
            IsLockedOut = false;
            ErrorMessage = "";
            _lockoutTimer.Stop();
        }
        else
        {
            ErrorMessage = $"Too many attempts. Try again in {LockoutRemainingSeconds}s";
        }
    }
}
