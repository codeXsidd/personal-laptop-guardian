using System.IO;
using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests;

[Collection("PinService")]
public sealed class PinSetupViewModelTests : IDisposable
{
    private readonly PinService _pinService;
    private readonly PinSetupViewModel _viewModel;
    private static readonly string PinDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopGuardian", "pin.dat");

    public PinSetupViewModelTests()
    {
        // Clean up any existing PIN
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);

        _pinService = new PinService();
        _viewModel = new PinSetupViewModel(_pinService);
    }

    public void Dispose()
    {
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);
    }

    [Fact]
    public void SetPin_WithValidMatchingPins_Succeeds()
    {
        _viewModel.NewPin = "1234";
        _viewModel.ConfirmPin = "1234";

        _viewModel.SetPinCommand.Execute(null);

        Assert.True(_viewModel.HasExistingPin);
        Assert.Contains("successfully", _viewModel.SuccessMessage);
        Assert.Empty(_viewModel.ErrorMessage);
    }

    [Fact]
    public void SetPin_WithMismatchedPins_ShowsError()
    {
        _viewModel.NewPin = "1234";
        _viewModel.ConfirmPin = "5678";

        _viewModel.SetPinCommand.Execute(null);

        Assert.False(_viewModel.HasExistingPin);
        Assert.Contains("do not match", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.SuccessMessage);
    }

    [Fact]
    public void SetPin_WithTooShortPin_ShowsError()
    {
        _viewModel.NewPin = "123";
        _viewModel.ConfirmPin = "123";

        _viewModel.SetPinCommand.Execute(null);

        Assert.False(_viewModel.HasExistingPin);
        Assert.Contains("at least 4 digits", _viewModel.ErrorMessage);
    }

    [Fact]
    public void SetPin_WithTooLongPin_ShowsError()
    {
        _viewModel.NewPin = "123456789";
        _viewModel.ConfirmPin = "123456789";

        _viewModel.SetPinCommand.Execute(null);

        Assert.False(_viewModel.HasExistingPin);
        Assert.Contains("cannot exceed 8 digits", _viewModel.ErrorMessage);
    }

    [Fact]
    public void SetPin_WithNonNumericPin_ShowsError()
    {
        _viewModel.NewPin = "12ab";
        _viewModel.ConfirmPin = "12ab";

        _viewModel.SetPinCommand.Execute(null);

        Assert.False(_viewModel.HasExistingPin);
        Assert.Contains("only digits", _viewModel.ErrorMessage);
    }

    [Fact]
    public void ChangePin_WithCorrectCurrentPin_Succeeds()
    {
        // Set initial PIN
        _pinService.SetPin("1234");
        _viewModel.Refresh();

        _viewModel.CurrentPin = "1234";
        _viewModel.NewPin = "5678";
        _viewModel.ConfirmPin = "5678";

        _viewModel.ChangePinCommand.Execute(null);

        Assert.Contains("successfully", _viewModel.SuccessMessage);
        Assert.Empty(_viewModel.ErrorMessage);
        Assert.True(_pinService.VerifyPin("5678"));
    }

    [Fact]
    public void ChangePin_WithIncorrectCurrentPin_ShowsError()
    {
        // Set initial PIN
        _pinService.SetPin("1234");
        _viewModel.Refresh();

        _viewModel.CurrentPin = "9999";
        _viewModel.NewPin = "5678";
        _viewModel.ConfirmPin = "5678";

        _viewModel.ChangePinCommand.Execute(null);

        Assert.Contains("incorrect", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.SuccessMessage);
    }

    [Fact]
    public void RemovePin_WithCorrectCurrentPin_Succeeds()
    {
        // Set initial PIN
        _pinService.SetPin("1234");
        _viewModel.Refresh();

        _viewModel.CurrentPin = "1234";

        _viewModel.RemovePinCommand.Execute(null);

        Assert.False(_viewModel.HasExistingPin);
        Assert.Contains("removed successfully", _viewModel.SuccessMessage);
        Assert.Empty(_viewModel.ErrorMessage);
    }

    [Fact]
    public void RemovePin_WithIncorrectCurrentPin_ShowsError()
    {
        // Set initial PIN
        _pinService.SetPin("1234");
        _viewModel.Refresh();

        _viewModel.CurrentPin = "9999";

        _viewModel.RemovePinCommand.Execute(null);

        Assert.True(_viewModel.HasExistingPin);
        Assert.Contains("incorrect", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.SuccessMessage);
    }

    [Fact]
    public void SetPin_RaisesPinChangedEvent()
    {
        var raised = false;
        _viewModel.PinChanged += (s, e) => raised = true;

        _viewModel.NewPin = "1234";
        _viewModel.ConfirmPin = "1234";
        _viewModel.SetPinCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void Refresh_UpdatesHasExistingPin()
    {
        Assert.False(_viewModel.HasExistingPin);

        _pinService.SetPin("1234");
        _viewModel.Refresh();

        Assert.True(_viewModel.HasExistingPin);
    }
}
