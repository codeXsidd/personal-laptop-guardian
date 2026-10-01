using System.IO;
using LaptopGuardian.Desktop.Services;
using Xunit;

namespace LaptopGuardian.Desktop.Tests;

[Collection("PinService")]
public sealed class PinServiceTests : IDisposable
{
    private readonly PinService _pinService;
    private static readonly string PinDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopGuardian", "pin.dat");

    public PinServiceTests()
    {
        _pinService = new PinService();
        // Clean up any existing PIN
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);
    }

    public void Dispose()
    {
        // Clean up after tests
        if (File.Exists(PinDataPath))
            File.Delete(PinDataPath);
    }

    [Fact]
    public void HasPin_WhenNoPinSet_ReturnsFalse()
    {
        Assert.False(_pinService.HasPin);
    }

    [Fact]
    public void SetPin_WithValidPin_CreatesFile()
    {
        _pinService.SetPin("1234");

        Assert.True(_pinService.HasPin);
        Assert.True(File.Exists(PinDataPath));
    }

    [Fact]
    public void VerifyPin_WithCorrectPin_ReturnsTrue()
    {
        _pinService.SetPin("1234");

        var result = _pinService.VerifyPin("1234");

        Assert.True(result);
    }

    [Fact]
    public void VerifyPin_WithIncorrectPin_ReturnsFalse()
    {
        _pinService.SetPin("1234");

        var result = _pinService.VerifyPin("5678");

        Assert.False(result);
    }

    [Fact]
    public void SetPin_TooShort_ThrowsException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _pinService.SetPin("123"));
        Assert.Contains("at least 4 digits", ex.Message);
    }

    [Fact]
    public void SetPin_TooLong_ThrowsException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _pinService.SetPin("123456789"));
        Assert.Contains("cannot exceed 8 digits", ex.Message);
    }

    [Fact]
    public void SetPin_NonNumeric_ThrowsException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _pinService.SetPin("12ab"));
        Assert.Contains("only digits", ex.Message);
    }

    [Fact]
    public void RemovePin_RemovesFile()
    {
        _pinService.SetPin("1234");
        Assert.True(_pinService.HasPin);

        _pinService.RemovePin();

        Assert.False(_pinService.HasPin);
        Assert.False(File.Exists(PinDataPath));
    }

    [Fact]
    public void VerifyPin_AfterFiveFailedAttempts_TriggersLockout()
    {
        _pinService.SetPin("1234");

        // 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            _pinService.VerifyPin("9999");
        }

        Assert.True(_pinService.IsLockedOut);
        Assert.True(_pinService.LockoutRemainingSeconds > 0);
    }

    [Fact]
    public void VerifyPin_DuringLockout_ReturnsFalse()
    {
        _pinService.SetPin("1234");

        // Trigger lockout
        for (int i = 0; i < 5; i++)
        {
            _pinService.VerifyPin("9999");
        }

        // Try correct PIN during lockout
        var result = _pinService.VerifyPin("1234");

        Assert.False(result);
        Assert.True(_pinService.IsLockedOut);
    }

    [Fact]
    public void VerifyPin_AfterCorrectAttempt_ResetsFailedAttempts()
    {
        _pinService.SetPin("1234");

        // Wrong attempts
        _pinService.VerifyPin("9999");
        _pinService.VerifyPin("9999");
        Assert.Equal(2, _pinService.FailedAttempts);

        // Correct attempt
        _pinService.VerifyPin("1234");

        Assert.Equal(0, _pinService.FailedAttempts);
    }

    [Fact]
    public void ResetLockout_ClearsLockout()
    {
        _pinService.SetPin("1234");

        // Trigger lockout
        for (int i = 0; i < 5; i++)
        {
            _pinService.VerifyPin("9999");
        }
        Assert.True(_pinService.IsLockedOut);

        _pinService.ResetLockout();

        Assert.False(_pinService.IsLockedOut);
        Assert.Equal(0, _pinService.FailedAttempts);
    }

    [Fact]
    public void SetPin_WithEightDigits_Succeeds()
    {
        _pinService.SetPin("12345678");

        Assert.True(_pinService.HasPin);
        Assert.True(_pinService.VerifyPin("12345678"));
    }

    [Fact]
    public void VerifyPin_WithEmptyString_ReturnsFalse()
    {
        _pinService.SetPin("1234");

        var result = _pinService.VerifyPin("");

        Assert.False(result);
    }
}
