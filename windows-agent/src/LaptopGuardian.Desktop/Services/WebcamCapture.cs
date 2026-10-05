using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;

namespace LaptopGuardian.Desktop.Services;

internal sealed class WebcamCapture : IDisposable
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private byte[]? _latestFrame;
    private readonly object _frameLock = new();
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    public async Task<bool> StartAsync()
    {
        if (_isRunning) return true;

        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (devices.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[Webcam] No camera devices found");
                return false;
            }

            _mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = devices[0].Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            };

            await _mediaCapture.InitializeAsync(settings);

            var frameSourceGroup = _mediaCapture.FrameSources;
            MediaFrameSource? videoSource = null;
            foreach (var kvp in frameSourceGroup)
            {
                if (kvp.Value.Info.MediaStreamType == MediaStreamType.VideoPreview ||
                    kvp.Value.Info.MediaStreamType == MediaStreamType.VideoRecord)
                {
                    videoSource = kvp.Value;
                    break;
                }
            }

            if (videoSource == null)
            {
                System.Diagnostics.Debug.WriteLine("[Webcam] No video source found");
                await StopAsync();
                return false;
            }

            _frameReader = await _mediaCapture.CreateFrameReaderAsync(
                videoSource,
                MediaEncodingSubtypes.Bgra8);

            _frameReader.FrameArrived += OnFrameArrived;
            var status = await _frameReader.StartAsync();

            if (status != MediaFrameReaderStartStatus.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[Webcam] Frame reader start failed: {status}");
                await StopAsync();
                return false;
            }

            _isRunning = true;
            System.Diagnostics.Debug.WriteLine("[Webcam] Camera started");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Webcam] Start error: {ex.Message}");
            await StopAsync();
            return false;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frameRef = sender.TryAcquireLatestFrame();
        if (frameRef?.VideoMediaFrame?.SoftwareBitmap == null) return;

        var softwareBitmap = frameRef.VideoMediaFrame.SoftwareBitmap;
        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        var w = softwareBitmap.PixelWidth;
        var h = softwareBitmap.PixelHeight;

        var buffer = new byte[w * h * 4];
        softwareBitmap.CopyToBuffer(buffer.AsBuffer());

        var scale = Math.Min(1.0, 480.0 / w);
        var scaledW = (int)(w * scale);
        var scaledH = (int)(h * scale);

        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bmpData = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
        bmp.UnlockBits(bmpData);

        using var scaled = new Bitmap(bmp, scaledW, scaledH);
        using var ms = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 50L);
        scaled.Save(ms, jpegEncoder, encoderParams);

        lock (_frameLock)
        {
            _latestFrame = ms.ToArray();
        }
    }

    public byte[]? GetLatestFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame;
        }
    }

    public async Task StopAsync()
    {
        _isRunning = false;

        if (_frameReader != null)
        {
            _frameReader.FrameArrived -= OnFrameArrived;
            await _frameReader.StopAsync();
            _frameReader.Dispose();
            _frameReader = null;
        }

        if (_mediaCapture != null)
        {
            _mediaCapture.Dispose();
            _mediaCapture = null;
        }

        lock (_frameLock)
        {
            _latestFrame = null;
        }

        System.Diagnostics.Debug.WriteLine("[Webcam] Camera stopped");
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
