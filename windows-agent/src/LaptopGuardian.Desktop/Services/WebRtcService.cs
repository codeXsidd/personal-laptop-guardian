using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace LaptopGuardian.Desktop.Services;

public sealed class WebRtcService : IDisposable
{
    private RTCPeerConnection? _pc;
    private RTCDataChannel? _dc;
    private FFmpegVideoEndPoint? _videoEndPoint;
    private CancellationTokenSource? _cts;
    private bool _capturing;

    private readonly Func<string, Task> _sendSignaling;

    private static bool _ffmpegInitialized;
    private static readonly object InitLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? SessionEnded;

    public bool IsActive => _pc?.connectionState == RTCPeerConnectionState.connected;

    /// <param name="sendSignaling">Callback to send signaling JSON through the relay WebSocket.</param>
    public WebRtcService(Func<string, Task> sendSignaling)
    {
        _sendSignaling = sendSignaling;
    }

    private static void EnsureFFmpegInitialized()
    {
        if (_ffmpegInitialized) return;
        lock (InitLock)
        {
            if (_ffmpegInitialized) return;
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_PANIC);
                _ffmpegInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebRTC] FFmpeg init failed: {ex.Message}");
                throw;
            }
        }
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        EnsureFFmpegInitialized();

        _videoEndPoint = new FFmpegVideoEndPoint();
        _videoEndPoint.RestrictFormats(f => f.Codec == VideoCodecsEnum.VP8);

        var config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>
            {
                new() { urls = "stun:stun.l.google.com:19302" },
                new() { urls = "stun:stun1.l.google.com:19302" },
            }
        };

        _pc = new RTCPeerConnection(config);

        var videoTrack = new MediaStreamTrack(
            _videoEndPoint.GetVideoSourceFormats(),
            MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(videoTrack);

        _videoEndPoint.OnVideoSourceEncodedSample += _pc.SendVideo;

        _pc.OnVideoFormatsNegotiated += (formats) =>
            _videoEndPoint.SetVideoSourceFormat(formats.First());

        _pc.ondatachannel += (dc) =>
        {
            _dc = dc;
            dc.onmessage += OnDataChannelMessage;
            StatusChanged?.Invoke(this, "DataChannel open");
        };

        _pc.onicecandidate += (candidate) =>
        {
            var json = JsonSerializer.Serialize(new
            {
                type = "webrtc_ice",
                candidate = candidate.ToString(),
                sdp_mid = candidate.sdpMid ?? "0",
                sdp_m_line_index = (int)candidate.sdpMLineIndex,
            });
            _ = _sendSignaling(json);
        };

        _pc.onconnectionstatechange += (state) =>
        {
            StatusChanged?.Invoke(this, $"WebRTC: {state}");
            if (state == RTCPeerConnectionState.connected)
            {
                _capturing = true;
                _ = Task.Run(CaptureLoop);
            }
            else if (state is RTCPeerConnectionState.failed
                     or RTCPeerConnectionState.disconnected
                     or RTCPeerConnectionState.closed)
            {
                _capturing = false;
                SessionEnded?.Invoke(this, EventArgs.Empty);
            }
        };

        StatusChanged?.Invoke(this, "WebRTC initialized, waiting for offer");
    }

    public async Task HandleSignalingMessageAsync(string json)
    {
        if (_pc == null) return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();

        switch (type)
        {
            case "webrtc_offer":
                var sdp = root.GetProperty("sdp").GetString();
                if (sdp != null)
                {
                    var offer = new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.offer,
                        sdp = sdp,
                    };
                    _pc.setRemoteDescription(offer);

                    var answer = _pc.createAnswer();
                    await _pc.setLocalDescription(answer);

                    var answerJson = JsonSerializer.Serialize(new
                    {
                        type = "webrtc_answer",
                        sdp = answer.sdp,
                    });
                    await _sendSignaling(answerJson);
                    StatusChanged?.Invoke(this, "WebRTC answer sent");
                }
                break;

            case "webrtc_ice":
                var candidateStr = root.GetProperty("candidate").GetString();
                if (candidateStr != null)
                {
                    var sdpMid = root.TryGetProperty("sdp_mid", out var midEl)
                        ? midEl.GetString() ?? "0" : "0";
                    ushort sdpMLineIdx = root.TryGetProperty("sdp_m_line_index", out var idxEl)
                        ? idxEl.GetUInt16() : (ushort)0;
                    _pc.addIceCandidate(new RTCIceCandidateInit
                    {
                        candidate = candidateStr,
                        sdpMid = sdpMid,
                        sdpMLineIndex = sdpMLineIdx,
                    });
                }
                break;
        }
    }

    private void CaptureLoop()
    {
        const int targetFps = 10;
        var delay = TimeSpan.FromMilliseconds(1000.0 / targetFps);
        const uint frameIntervalMs = 1000 / targetFps;

        while (_capturing && _pc?.connectionState == RTCPeerConnectionState.connected)
        {
            try
            {
                var (width, height, rawBgr) = CaptureScreenRaw();
                if (rawBgr != null && _videoEndPoint != null)
                {
                    _videoEndPoint.ExternalVideoSourceRawSample(
                        frameIntervalMs, width, height, rawBgr,
                        VideoPixelFormatsEnum.Bgr);
                }
                Thread.Sleep(delay);
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }

    private static (int width, int height, byte[]? data) CaptureScreenRaw()
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

            var bmpData = scaledBmp.LockBits(
                new Rectangle(0, 0, captureW, captureH),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                var rawSize = Math.Abs(bmpData.Stride) * captureH;
                var raw = new byte[rawSize];
                Marshal.Copy(bmpData.Scan0, raw, 0, rawSize);
                return (captureW, captureH, raw);
            }
            finally
            {
                scaledBmp.UnlockBits(bmpData);
            }
        }
        catch
        {
            return (0, 0, null);
        }
    }

    private void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols proto, byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var msg = JsonSerializer.Deserialize<RelayMessage>(json, JsonOpts);
            if (msg == null) return;

            if (msg.Type == "input")
                HandleInput(msg);
            else if (msg.Type == "pc_control")
                Application.Current?.Dispatcher.Invoke(() => HandlePcControl(msg));
        }
        catch { }
    }

    private static void HandleInput(RelayMessage msg)
    {
        if (msg.InputType is "mouse_move" or "click" or "double_click" or "right_click")
        {
            var absX = (int)(msg.NX * 65535);
            var absY = (int)(msg.NY * 65535);
            var moveInput = InputHelper.CreateMouseMoveInput(absX, absY);
            InputHelper.SendInput(moveInput);

            if (msg.InputType == "click")
                InputHelper.SendMouseClick(isRight: false);
            else if (msg.InputType == "right_click")
                InputHelper.SendMouseClick(isRight: true);
            else if (msg.InputType == "double_click")
            {
                InputHelper.SendMouseClick(isRight: false);
                InputHelper.SendMouseClick(isRight: false);
            }
        }
        else if (msg.InputType == "scroll")
        {
            InputHelper.SendMouseScroll(msg.DeltaY);
        }
        else if (msg.InputType == "key_down")
        {
            InputHelper.SendKeyPress(msg.Key, down: true);
        }
        else if (msg.InputType == "key_up")
        {
            InputHelper.SendKeyPress(msg.Key, down: false);
        }
    }

    private void HandlePcControl(RelayMessage msg)
    {
        var action = msg.Action;
        if (string.IsNullOrEmpty(action)) return;

        StatusChanged?.Invoke(this, $"PC control: {action}");

        switch (action)
        {
            case "lock":
                InputHelper.LockWorkStation();
                break;
            case "sleep":
                InputHelper.SetSuspendState(false, false, false);
                break;
            case "restart":
                Stop();
                Task.Delay(500).ContinueWith(_ => InputHelper.InitiateShutdown(restart: true));
                break;
            case "shutdown":
                Stop();
                Task.Delay(500).ContinueWith(_ => InputHelper.InitiateShutdown(restart: false));
                break;
        }
    }

    public void Stop()
    {
        _capturing = false;
        _cts?.Cancel();

        _dc?.close();
        _dc = null;

        _videoEndPoint?.Dispose();
        _videoEndPoint = null;

        _pc?.close();
        _pc = null;

        StatusChanged?.Invoke(this, "WebRTC stopped");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
