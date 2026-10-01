using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
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
    private string? _currentSessionId;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private DateTime _sessionStartedAt;

    public event EventHandler<RemoteSessionRequest>? SessionRequested;
    public event EventHandler<string>? SessionEnded;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Error;

    public bool IsActive => _ws?.State == WebSocketState.Open && _currentSessionId != null;
    public string? CurrentSessionId => _currentSessionId;

    public DateTime SessionStartedAt => _sessionStartedAt;

    public RemoteAccessService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (s, e) => await PollForSessionsAsync();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var response = await _ipc.SendAsync("get-remote-config");
        if (!response.Success)
        {
            Error?.Invoke(this, "Failed to get remote config from agent");
            return;
        }

        _deviceId = response.GetString("device_id");
        _apiKey = response.GetString("api_key");
        _supabaseUrl = response.GetString("supabase_url");

        if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_apiKey))
        {
            Error?.Invoke(this, "Device not registered or missing configuration");
            return;
        }

        _pollTimer.Start();
    }

    public void StopPolling() => _pollTimer.Stop();

    private async Task PollForSessionsAsync()
    {
        if (_supabaseUrl == null || _apiKey == null) return;
        if (_currentSessionId != null) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_supabaseUrl}/functions/v1/manage-remote-session");
            req.Headers.Add("x-device-api-key", _apiKey);

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            var result = await resp.Content.ReadFromJsonAsync<SessionsResponse>(JsonOpts);
            var pending = result?.Sessions?.FirstOrDefault(s => s.Status == "pending");
            if (pending == null) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                SessionRequested?.Invoke(this, new RemoteSessionRequest
                {
                    SessionId = pending.Id,
                    ExpiresAt = pending.ExpiresAt,
                });
            });
        }
        catch { }
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
            StatusChanged?.Invoke(this, "Session approved. Connecting...");

            await ConnectRelayAsync(sessionId);
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

    private async Task ConnectRelayAsync(string sessionId)
    {
        _wsCts?.Cancel();
        _wsCts = new CancellationTokenSource();
        var ct = _wsCts.Token;

        try
        {
            var wsUrl = _supabaseUrl!.Replace("https://", "wss://").Replace("http://", "ws://");
            var uri = new Uri(
                $"{wsUrl}/functions/v1/remote-relay?session_id={sessionId}&role=desktop&api_key={Uri.EscapeDataString(_apiKey!)}");

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(uri, ct);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusChanged?.Invoke(this, "Connected to relay. Streaming screen...");
            });

            // Activate the session
            _ = ActivateSessionAsync(sessionId);

            // Start screen capture + send in background
            _ = Task.Run(() => CaptureAndSendLoop(ct), ct);

            // Start receiving input
            _ = Task.Run(() => ReceiveLoop(ct), ct);
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                Error?.Invoke(this, $"Relay connection failed: {ex.Message}"));
            await EndSessionAsync("connection_failed");
        }
    }

    private async Task CaptureAndSendLoop(CancellationToken ct)
    {
        const int targetFps = 8;
        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var frame = CaptureScreen();
                if (frame != null && frame.Length > 0)
                {
                    await _ws.SendAsync(frame, WebSocketMessageType.Binary, true, ct);
                }
                await Task.Delay(frameDelay, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch { await Task.Delay(500, ct); }
        }

        if (!ct.IsCancellationRequested)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                _ = EndSessionAsync("connection_lost"));
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = JsonSerializer.Deserialize<RelayMessage>(json, JsonOpts);

                if (msg?.Type == "peer_left")
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        _ = EndSessionAsync("mobile_disconnected"));
                    break;
                }

                if (msg?.Type == "input")
                {
                    HandleInput(msg);
                }
                else if (msg?.Type == "pc_control")
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        HandlePcControl(msg));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch { }
        }
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
        // Screen capture is implemented at runtime to avoid
        // static references that may trigger Application Control policies.
        return ScreenCapture.Capture();
    }

    public async Task EndSessionAsync(string reason = "ended_normally")
    {
        _wsCts?.Cancel();

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason,
                    new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
            }
            catch { }
        }
        _ws?.Dispose();
        _ws = null;

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
        _wsCts?.Cancel();
        _ws?.Dispose();
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
}

internal sealed class RemoteSessionDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("expires_at")] public DateTime ExpiresAt { get; set; }
}
