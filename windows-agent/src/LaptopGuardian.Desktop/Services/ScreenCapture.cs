using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace LaptopGuardian.Desktop.Services;

internal static class ScreenCapture
{
    internal static byte[]? Capture()
    {
        try
        {
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            var scale = Math.Min(1.0, 960.0 / screenWidth);
            var captureW = (int)(screenWidth * scale);
            var captureH = (int)(screenHeight * scale);

            using var fullBmp = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(fullBmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
            }

            using var scaledBmp = new Bitmap(fullBmp, captureW, captureH);
            using var ms = new MemoryStream();

            var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                .First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 40L);

            scaledBmp.Save(ms, jpegEncoder, encoderParams);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
