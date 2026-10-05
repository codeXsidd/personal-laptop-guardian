using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace LaptopGuardian.Desktop.Services;

public sealed class RemoteAccessService : IDisposable
{
    private readonly IpcClient _ipc = new();
    private readonly HttpClient _http = new();
    private readonly DispatcherTimer _pollTimer;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private bool _initialized;
    private string? _deviceId;
    private string? _apiKey;
    private string? _supabaseUrl;
    private string? _supabaseAnonKey;
    private string? _currentSessionId;
    private RealtimeRelay? _relay;
    private CancellationTokenSource? _relayCts;
    private DateTime _sessionStartedAt;
    private WebcamCapture? _webcam;
    private CancellationTokenSource? _cameraCts;

    public event EventHandler<RemoteSessionRequest>? SessionRequested;
    public event EventHandler<string>? SessionEnded;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Error;
    public event EventHandler<PcControlResult>? PcControlExecuted;
    public event EventHandler<bool>? CameraStateChanged;

    public bool IsActive => _relay?.IsConnected == true && _currentSessionId != null;
    public bool IsCameraActive => _webcam?.IsRunning == true;
    public string? CurrentSessionId => _currentSessionId;

    public DateTime SessionStartedAt => _sessionStartedAt;

    public RemoteAccessService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (s, e) => await PollForSessionsAsync();
    }

    private static void DebugLog(string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        DebugLog("InitializeAsync starting");
        var response = await _ipc.SendAsync("get-remote-config");
        if (!response.Success)
        {
            DebugLog($"IPC failed: {response.ErrorMessage}");
            Error?.Invoke(this, "Failed to get remote config from agent");
            return;
        }

        _deviceId = response.GetString("device_id");
        _apiKey = response.GetString("api_key");
        _supabaseUrl = response.GetString("supabase_url");
        _supabaseAnonKey = response.GetString("supabase_anon_key");

        DebugLog($"Config: url={_supabaseUrl}, apiKey={(_apiKey?.Length > 10 ? _apiKey[..10] + "..." : "null")}, anonKey={((_supabaseAnonKey?.Length ?? 0) > 10 ? "present" : "null")}");

        if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_apiKey))
        {
            Error?.Invoke(this, "Device not registered or missing configuration");
            return;
        }

        if (string.IsNullOrEmpty(_supabaseAnonKey))
        {
            Error?.Invoke(this, "Missing Supabase anon key — update the Windows agent service");
            return;
        }

        DebugLog("Starting poll timer");
        _pollTimer.Start();
    }

    public void StopPolling() => _pollTimer.Stop();

    private async Task PollForSessionsAsync()
    {
        if (_supabaseUrl == null || _apiKey == null) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                DebugLog($"Poll HTTP {resp.StatusCode}");
                return;
            }

            var result = await resp.Content.ReadFromJsonAsync<SessionsResponse>(JsonOpts);
            var sessions = result?.Sessions ?? [];
            DebugLog($"Poll: {sessions.Length} sessions, currentId={_currentSessionId}");

            if (_currentSessionId != null)
            {
                var currentStillActive = sessions.Any(s =>
                    s.Id == _currentSessionId &&
                    s.Status is "approved" or "active");
                if (!currentStillActive)
                {
                    DebugLog("Current session no longer active — ending");
                    await EndSessionAsync("ended_externally");
                }
                return;
            }

            var pending = sessions.FirstOrDefault(s => s.Status == "pending");
            if (pending != null)
            {
                DebugLog($"Pending session found: {pending.Id}");
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    SessionRequested?.Invoke(this, new RemoteSessionRequest
                    {
                        SessionId = pending.Id,
                        ExpiresAt = pending.ExpiresAt,
                    });
                });
            }

            // Process pending PC control commands
            var commands = result?.Commands ?? [];
            foreach (var cmd in commands)
            {
                if (cmd.Status != "pending") continue;
                DebugLog($"Executing PC control: {cmd.Action} (id={cmd.Id})");
                Application.Current?.Dispatcher.Invoke(() =>
                    ExecutePcControlCommand(cmd));
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Poll error: {ex.Message}");
        }
    }

    private async void ExecutePcControlCommand(PcControlCommandDto cmd)
    {
        string resultText = "ok";
        try
        {
            switch (cmd.Action)
            {
                case "lock":
                    InputHelper.LockWorkStation();
                    break;
                case "sleep":
                    InputHelper.SetSuspendState(false, false, false);
                    break;
                case "restart":
                    _ = Task.Delay(1000).ContinueWith(_ => InputHelper.InitiateShutdown(restart: true));
                    break;
                case "shutdown":
                    _ = Task.Delay(1000).ContinueWith(_ => InputHelper.InitiateShutdown(restart: false));
                    break;
                default:
                    resultText = $"unknown action: {cmd.Action}";
                    break;
            }

            PcControlExecuted?.Invoke(this, new PcControlResult
            {
                Action = cmd.Action,
                Success = resultText == "ok",
                Message = resultText == "ok" ? $"PC {cmd.Action} executed" : resultText,
            });
        }
        catch (Exception ex)
        {
            resultText = ex.Message;
            DebugLog($"PC control error: {ex.Message}");
        }

        await AcknowledgePcControlAsync(cmd.Id, resultText);
    }

    private async Task AcknowledgePcControlAsync(string commandId, string result)
    {
        if (_supabaseUrl == null || _apiKey == null) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);
            req.Content = JsonContent.Create(new
            {
                action = "ack_pc_control",
                command_id = commandId,
                result,
            });
            await _http.SendAsync(req);
        }
        catch (Exception ex)
        {
            DebugLog($"Ack PC control error: {ex.Message}");
        }
    }

    public async Task ApproveSessionAsync(string sessionId)
    {
        if (_supabaseUrl == null || _apiKey == null) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);
            req.Content = JsonContent.Create(new { action = "approve", session_id = sessionId });

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Error?.Invoke(this, "Failed to approve session");
                return;
            }

            _currentSessionId = sessionId;
            _sessionStartedAt = DateTime.UtcNow;
            StatusChanged?.Invoke(this, "Session approved. Connecting via Realtime...");

            await ConnectRealtimeAsync(sessionId);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"Approve failed: {ex.Message}");
        }
    }

    public async Task RejectSessionAsync(string sessionId)
    {
        if (_supabaseUrl == null || _apiKey == null) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);
            req.Content = JsonContent.Create(new { action = "reject", session_id = sessionId });

            await _http.SendAsync(req);
            StatusChanged?.Invoke(this, "Session rejected");
        }
        catch { }
    }

    private async Task ConnectRealtimeAsync(string sessionId)
    {
        DebugLog($"ConnectRealtimeAsync: session={sessionId}");
        _relayCts?.Cancel();
        _relayCts = new CancellationTokenSource();
        var ct = _relayCts.Token;

        try
        {
            _relay = new RealtimeRelay(_supabaseUrl!, _supabaseAnonKey!);

            _relay.StatusChanged += (_, status) =>
            {
                DebugLog($"Relay status: {status}");
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusChanged?.Invoke(this, status));
            };

            _relay.TextMessageReceived += (_, payload) =>
            {
                DebugLog($"Relay msg: {payload[..Math.Min(100, payload.Length)]}");
                HandleRelayMessage(payload);
            };

            _relay.Disconnected += (_, _) =>
            {
                DebugLog("Relay disconnected");
                if (!ct.IsCancellationRequested)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        _ = EndSessionAsync("relay_disconnected"));
                }
            };

            await _relay.ConnectAsync(sessionId, ct);
            DebugLog($"Relay connected, IsConnected={_relay.IsConnected}");

            Application.Current?.Dispatcher.Invoke(() =>
                StatusChanged?.Invoke(this, "Connected to Realtime. Streaming screen..."));

            _ = ActivateSessionAsync(sessionId);
            _ = Task.Run(() => CaptureAndSendLoop(ct), ct);
        }
        catch (Exception ex)
        {
            DebugLog($"Realtime connect error: {ex}");
            Application.Current?.Dispatcher.Invoke(() =>
                Error?.Invoke(this, $"Realtime connection failed: {ex.Message}"));
            await EndSessionAsync("connection_failed");
        }
    }

    private async Task CaptureAndSendLoop(CancellationToken ct)
    {
        const int targetFps = 5;
        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / targetFps);
        int frameCount = 0;

        DebugLog($"CaptureAndSendLoop starting, relay connected={_relay?.IsConnected}");

        while (!ct.IsCancellationRequested && _relay?.IsConnected == true)
        {
            try
            {
                var frame = CaptureScreen();
                if (frame != null && frame.Length > 0)
                {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 50 == 0)
                        DebugLog($"Sending frame #{frameCount} size={frame.Length}");
                    await _relay.SendFrameAsync(frame, ct);
                }
                else if (frameCount == 0)
                {
                    DebugLog("CaptureScreen returned null/empty");
                }
                await Task.Delay(frameDelay, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                DebugLog($"Capture error: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
        DebugLog($"CaptureAndSendLoop exited, frames sent={frameCount}");

        if (!ct.IsCancellationRequested)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                _ = EndSessionAsync("connection_lost"));
        }
    }

    private void HandleRelayMessage(string payloadJson)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<RelayMessage>(payloadJson, JsonOpts);
            if (msg == null) return;

            if (msg.Type == "input")
            {
                HandleInput(msg);
            }
            else if (msg.Type == "pc_control")
            {
                Application.Current?.Dispatcher.Invoke(() =>
                    HandlePcControl(msg));
            }
            else if (msg.Type == "camera_control")
            {
                _ = HandleCameraControl(msg);
            }
        }
        catch { }
    }

    private static void HandleInput(RelayMessage msg)
    {
        if (msg.InputType == "mouse_move" || msg.InputType == "click" ||
            msg.InputType == "double_click" || msg.InputType == "right_click")
        {
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;
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
                _ = EndSessionAsync("restart_requested");
                Task.Delay(500).ContinueWith(_ => InputHelper.InitiateShutdown(restart: true));
                break;
            case "shutdown":
                _ = EndSessionAsync("shutdown_requested");
                Task.Delay(500).ContinueWith(_ => InputHelper.InitiateShutdown(restart: false));
                break;
        }
    }

    private async Task ActivateSessionAsync(string sessionId)
    {
        if (_supabaseUrl == null || _apiKey == null) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);
            req.Content = JsonContent.Create(new { action = "activate", session_id = sessionId });
            await _http.SendAsync(req);
        }
        catch { }
    }

    private static byte[]? CaptureScreen()
    {
        return ScreenCapture.Capture();
    }

    private async Task HandleCameraControl(RelayMessage msg)
    {
        var action = msg.Action;
        if (action == "start")
            await StartCameraAsync();
        else if (action == "stop")
            await StopCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        if (_webcam?.IsRunning == true) return;

        _webcam = new WebcamCapture();
        var started = await _webcam.StartAsync();
        if (!started)
        {
            DebugLog("Camera: failed to start");
            _webcam.Dispose();
            _webcam = null;
            Application.Current?.Dispatcher.Invoke(() =>
                CameraStateChanged?.Invoke(this, false));
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
            CameraStateChanged?.Invoke(this, true));

        _cameraCts = new CancellationTokenSource();
        _ = Task.Run(() => CameraFrameLoop(_cameraCts.Token), _cameraCts.Token);
    }

    private async Task CameraFrameLoop(CancellationToken ct)
    {
        const int targetFps = 5;
        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        while (!ct.IsCancellationRequested && _webcam?.IsRunning == true && _relay?.IsConnected == true)
        {
            try
            {
                var frame = _webcam.GetLatestFrame();
                if (frame is { Length: > 0 })
                {
                    await _relay!.SendBroadcastAsync("camera_frame",
                        Convert.ToBase64String(frame), ct);
                }
                await Task.Delay(frameDelay, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                DebugLog($"Camera frame error: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
    }

    private async Task StopCameraAsync()
    {
        _cameraCts?.Cancel();
        if (_webcam != null)
        {
            await _webcam.StopAsync();
            _webcam.Dispose();
            _webcam = null;
        }
        Application.Current?.Dispatcher.Invoke(() =>
            CameraStateChanged?.Invoke(this, false));
    }

    public async Task EndSessionAsync(string reason = "ended_normally")
    {
        await StopCameraAsync();
        _relayCts?.Cancel();

        if (_relay != null)
        {
            try { await _relay.DisconnectAsync(); } catch { }
            _relay.Dispose();
            _relay = null;
        }

        if (_supabaseUrl != null && _apiKey != null && _currentSessionId != null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"{_supabaseUrl}/functions/v1/manage-remote-session");
                req.Headers.Add("x-device-api-key", _apiKey);
                req.Content = JsonContent.Create(new
                {
                    action = "end",
                    session_id = _currentSessionId,
                });
                await _http.SendAsync(req);
            }
            catch { }
        }

        var sessionId = _currentSessionId;
        _currentSessionId = null;

        SessionEnded?.Invoke(this, reason);
        StatusChanged?.Invoke(this, "Session ended");
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _relayCts?.Cancel();
        _relay?.Dispose();
        _http.Dispose();
    }

}

public sealed class RemoteSessionRequest
{
    public string SessionId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

internal sealed class RelayMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("input_type")] public string? InputType { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("nx")] public double NX { get; set; }
    [JsonPropertyName("ny")] public double NY { get; set; }
    [JsonPropertyName("delta_y")] public int DeltaY { get; set; }
    [JsonPropertyName("button")] public string? Button { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
}

internal sealed class SessionsResponse
{
    [JsonPropertyName("sessions")] public RemoteSessionDto[]? Sessions { get; set; }
    [JsonPropertyName("commands")] public PcControlCommandDto[]? Commands { get; set; }
}

internal sealed class RemoteSessionDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("expires_at")] public DateTime ExpiresAt { get; set; }
}

internal sealed class PcControlCommandDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

public sealed class PcControlResult
{
    public string Action { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
