using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace LaptopGuardian.Desktop.Services;

public static class QrCodeGenerator
{
    public static BitmapSource GenerateQrCode(string pairingCode, int pixelsPerModule = 10)
    {
        var payload = $"laptopguardian://pair?code={pairingCode}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCode(data);
        using var bitmap = qrCode.GetGraphic(pixelsPerModule, Color.FromArgb(26, 115, 232), Color.White, true);

        var bitmapImage = new BitmapImage();
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
        }
        bitmapImage.Freeze();
        return bitmapImage;
    }
}
