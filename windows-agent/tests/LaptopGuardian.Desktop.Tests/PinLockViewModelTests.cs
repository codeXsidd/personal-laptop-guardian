using System.IO;
using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests;

[Collection("PinService")]
public sealed class PinLockViewModelTests : IDisposable
{
    private readonly PinService _pinService;
    private readonly PinLockViewModel _viewModel;
    private static readonly string PinDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopGuardian", "pin.dat");

    public PinLockViewModelTests()
    {
        // Clean up any existing PIN
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);

        _pinService = new PinService();
        _pinService.SetPin("1234");
        _viewModel = new PinLockViewModel(_pinService);
    }

    public void Dispose()
    {
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);
    }

    [Fact]
    public void AddDigit_AddsDigitToPin()
    {
        _viewModel.AddDigitCommand.Execute("1");
        _viewModel.AddDigitCommand.Execute("2");

        Assert.Equal("12", _viewModel.Pin);
    }

    [Fact]
    public void Backspace_RemovesLastDigit()
    {
        _viewModel.AddDigitCommand.Execute("1");
        _viewModel.AddDigitCommand.Execute("2");
        _viewModel.AddDigitCommand.Execute("3");

        _viewModel.BackspaceCommand.Execute(null);

        Assert.Equal("12", _viewModel.Pin);
    }

    [Fact]
    public void Clear_ClearsPin()
    {
        _viewModel.AddDigitCommand.Execute("1");
        _viewModel.AddDigitCommand.Execute("2");

        _viewModel.ClearCommand.Execute(null);

        Assert.Equal("", _viewModel.Pin);
    }

    [Fact]
    public void Unlock_WithCorrectPin_UnlocksAndRaisesEvent()
    {
        var unlocked = false;
        _viewModel.Unlocked += (s, e) => unlocked = true;

        _viewModel.AddDigitCommand.Execute("1");
        _viewModel.AddDigitCommand.Execute("2");
        _viewModel.AddDigitCommand.Execute("3");
        _viewModel.AddDigitCommand.Execute("4");
        _viewModel.UnlockCommand.Execute(null);

        Assert.False(_viewModel.IsLocked);
        Assert.True(unlocked);
        Assert.Empty(_viewModel.ErrorMessage);
    }

    [Fact]
    public void Unlock_WithIncorrectPin_ShowsError()
    {
        _viewModel.AddDigitCommand.Execute("9");
        _viewModel.AddDigitCommand.Execute("9");
        _viewModel.AddDigitCommand.Execute("9");
        _viewModel.AddDigitCommand.Execute("9");
        _viewModel.UnlockCommand.Execute(null);

        Assert.True(_viewModel.IsLocked);
        Assert.Contains("Incorrect PIN", _viewModel.ErrorMessage);
        Assert.Empty(_viewModel.Pin);
    }

    [Fact]
    public void Unlock_WithEmptyPin_ShowsError()
    {
        _viewModel.UnlockCommand.Execute(null);

        Assert.True(_viewModel.IsLocked);
        Assert.Contains("enter your PIN", _viewModel.ErrorMessage);
    }

    [Fact]
    public void Unlock_WithTooShortPin_ShowsError()
    {
        _viewModel.AddDigitCommand.Execute("1");
        _viewModel.AddDigitCommand.Execute("2");
        _viewModel.UnlockCommand.Execute(null);

        Assert.True(_viewModel.IsLocked);
        Assert.Contains("at least 4 digits", _viewModel.ErrorMessage);
    }

    [Fact]
    public void Unlock_AfterFiveFailedAttempts_ShowsLockoutMessage()
    {
        for (int i = 0; i < 5; i++)
        {
            _viewModel.Pin = "9999";
            _viewModel.UnlockCommand.Execute(null);
        }

        Assert.True(_viewModel.IsLockedOut);
        Assert.Contains("Too many attempts", _viewModel.ErrorMessage);
    }

    [Fact]
    public void Lock_ResetsState()
    {
        _viewModel.Pin = "12";
        _viewModel.ErrorMessage = "Some error";

        _viewModel.Lock();

        Assert.True(_viewModel.IsLocked);
        Assert.Empty(_viewModel.Pin);
        Assert.Empty(_viewModel.ErrorMessage);
    }

    [Fact]
    public void AddDigit_ExceedingEightDigits_DoesNotAdd()
    {
        // Add 7 digits - should work fine
        for (int i = 0; i < 7; i++)
        {
            _viewModel.AddDigitCommand.Execute("1");
        }
        Assert.Equal(7, _viewModel.Pin.Length);

        // Try to add 2 more - should only add 1 (max is 8)
        _viewModel.AddDigitCommand.Execute("1");
        // At 8 digits, auto-submit triggers
        // Since PIN is wrong, it gets cleared and shows error
        Assert.True(_viewModel.IsLocked);

        // Verify that we can't add beyond 8 before auto-submit
        _viewModel.Pin = "12345678"; // Set to 8 manually
        _viewModel.AddDigitCommand.Execute("9");
        Assert.Equal(8, _viewModel.Pin.Length); // Should still be 8, not 9
    }
}
