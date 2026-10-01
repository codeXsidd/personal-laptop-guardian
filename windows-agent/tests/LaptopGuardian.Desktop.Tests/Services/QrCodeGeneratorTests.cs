using LaptopGuardian.Desktop.Services;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.Services;

public class QrCodeGeneratorTests
{
    [Fact]
    public void GenerateQrCode_ReturnsNonNullBitmapSource()
    {
        var result = QrCodeGenerator.GenerateQrCode("ABC123");
        Assert.NotNull(result);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    [Fact]
    public void GenerateQrCode_IsFrozen()
    {
        var result = QrCodeGenerator.GenerateQrCode("XYZ789");
        Assert.True(result.IsFrozen);
    }

    [Theory]
    [InlineData("A1B2C3")]
    [InlineData("ZZZZZZ")]
    [InlineData("123456")]
    public void GenerateQrCode_AcceptsVariousCodes(string code)
    {
        var result = QrCodeGenerator.GenerateQrCode(code);
        Assert.NotNull(result);
    }

    [Fact]
    public void GenerateQrCode_DifferentPixelsPerModule_ChangesSize()
    {
        var small = QrCodeGenerator.GenerateQrCode("TEST", pixelsPerModule: 5);
        var large = QrCodeGenerator.GenerateQrCode("TEST", pixelsPerModule: 20);
        Assert.True(large.Width > small.Width);
    }
}
