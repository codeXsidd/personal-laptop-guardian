using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaptopGuardian.Desktop.Services;

namespace LaptopGuardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for setting up, changing, or removing a PIN.
/// </summary>
public partial class PinSetupViewModel : ObservableObject
{
    private readonly PinService _pinService;

    [ObservableProperty]
    private string _currentPin = "";

    [ObservableProperty]
    private string _newPin = "";

    [ObservableProperty]
    private string _confirmPin = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _successMessage = "";

    [ObservableProperty]
    private bool _hasExistingPin;

    public event EventHandler? PinChanged;

    public PinSetupViewModel(PinService pinService)
    {
        _pinService = pinService;
        HasExistingPin = pinService.HasPin;
    }

    [RelayCommand]
    private void SetPin()
    {
        ErrorMessage = "";
        SuccessMessage = "";

        // Validation
        if (string.IsNullOrEmpty(NewPin))
        {
            ErrorMessage = "Please enter a PIN";
            return;
        }

        if (NewPin.Length < 4)
        {
            ErrorMessage = "PIN must be at least 4 digits";
            return;
        }

        if (NewPin.Length > 8)
        {
            ErrorMessage = "PIN cannot exceed 8 digits";
            return;
        }

        if (!NewPin.All(char.IsDigit))
        {
            ErrorMessage = "PIN must contain only digits";
            return;
        }

        if (NewPin != ConfirmPin)
        {
            ErrorMessage = "PINs do not match";
            return;
        }

        try
        {
            _pinService.SetPin(NewPin);
            HasExistingPin = true;
            SuccessMessage = "PIN set successfully";
            CurrentPin = "";
            NewPin = "";
            ConfirmPin = "";
            PinChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to set PIN: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ChangePin()
    {
        ErrorMessage = "";
        SuccessMessage = "";

        // Verify current PIN first
        if (string.IsNullOrEmpty(CurrentPin))
        {
            ErrorMessage = "Please enter your current PIN";
            return;
        }

        if (!_pinService.VerifyPin(CurrentPin))
        {
            ErrorMessage = "Current PIN is incorrect";
            return;
        }

        // Validation for new PIN
        if (string.IsNullOrEmpty(NewPin))
        {
            ErrorMessage = "Please enter a new PIN";
            return;
        }

        if (NewPin.Length < 4)
        {
            ErrorMessage = "New PIN must be at least 4 digits";
            return;
        }

        if (NewPin.Length > 8)
        {
            ErrorMessage = "New PIN cannot exceed 8 digits";
            return;
        }

        if (!NewPin.All(char.IsDigit))
        {
            ErrorMessage = "New PIN must contain only digits";
            return;
        }

        if (NewPin != ConfirmPin)
        {
            ErrorMessage = "New PINs do not match";
            return;
        }

        try
        {
            _pinService.SetPin(NewPin);
            SuccessMessage = "PIN changed successfully";
            CurrentPin = "";
            NewPin = "";
            ConfirmPin = "";
            PinChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to change PIN: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemovePin()
    {
        ErrorMessage = "";
        SuccessMessage = "";

        // Verify current PIN first
        if (string.IsNullOrEmpty(CurrentPin))
        {
            ErrorMessage = "Please enter your current PIN to remove it";
            return;
        }

        if (!_pinService.VerifyPin(CurrentPin))
        {
            ErrorMessage = "Current PIN is incorrect";
            return;
        }

        try
        {
            _pinService.RemovePin();
            HasExistingPin = false;
            SuccessMessage = "PIN removed successfully";
            CurrentPin = "";
            NewPin = "";
            ConfirmPin = "";
            PinChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove PIN: {ex.Message}";
        }
    }

    public void Refresh()
    {
        HasExistingPin = _pinService.HasPin;
        CurrentPin = "";
        NewPin = "";
        ConfirmPin = "";
        ErrorMessage = "";
        SuccessMessage = "";
    }
}
