using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaptopGuardian.Desktop.Services;

/// <summary>
/// Connects to Supabase Realtime broadcast channel for relaying screen frames
/// and input events. Uses the Phoenix WebSocket protocol.
/// </summary>
public sealed class RealtimeRelay : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly string _supabaseUrl;
    private readonly string _anonKey;
    private string? _channelTopic;
    private int _ref;
    private bool _joined;
    private TaskCompletionSource<bool>? _joinTcs;

    public event EventHandler<string>? TextMessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? Disconnected;
    public bool IsConnected => _ws?.State == WebSocketState.Open && _joined;

    public RealtimeRelay(string supabaseUrl, string anonKey)
    {
        _supabaseUrl = supabaseUrl;
        _anonKey = anonKey;
    }

    public async Task ConnectAsync(string sessionId, CancellationToken ct)
    {
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        var wsUrl = _supabaseUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://");
        var uri = new Uri($"{wsUrl}/realtime/v1/websocket?apikey={Uri.EscapeDataString(_anonKey)}&vsn=1.0.0");

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(uri, token);

        _channelTopic = $"realtime:remote-session-{sessionId}";
        _joined = false;
        _ref = 0;

        // Start receive loop
        _ = Task.Run(() => ReceiveLoopAsync(token), token);

        // Start heartbeat
        _ = Task.Run(() => HeartbeatLoopAsync(token), token);

        // Join the broadcast channel and wait for confirmation
        _joinTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await JoinChannelAsync(token);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        timeoutCts.Token.Register(() => _joinTcs.TrySetResult(false));
        var joined = await _joinTcs.Task;
        if (!joined)
            throw new TimeoutException("Failed to join Realtime channel within 10 seconds");
    }

    private async Task JoinChannelAsync(CancellationToken ct)
    {
        var join = new PhoenixMessage
        {
            JoinRef = NextRef(),
            Ref = NextRef(),
            Topic = _channelTopic!,
            Event = "phx_join",
            Payload = new Dictionary<string, object>
            {
                ["config"] = new Dictionary<string, object>
                {
                    ["broadcast"] = new Dictionary<string, object>
                    {
                        ["self"] = false,
                    },
                },
            },
        };
        await SendJsonAsync(join, ct);
    }

    public async Task SendFrameAsync(byte[] jpegFrame, CancellationToken ct)
    {
        if (!IsConnected) return;

        var base64 = Convert.ToBase64String(jpegFrame);
        var msg = new PhoenixMessage
        {
            JoinRef = null,
            Ref = NextRef(),
            Topic = _channelTopic!,
            Event = "broadcast",
            Payload = new Dictionary<string, object>
            {
                ["type"] = "broadcast",
                ["event"] = "frame",
                ["payload"] = new Dictionary<string, object>
                {
                    ["data"] = base64,
                },
            },
        };
        await SendJsonAsync(msg, ct);
    }

    public async Task SendBroadcastAsync(string eventName, string data, CancellationToken ct)
    {
        if (!IsConnected) return;

        var msg = new PhoenixMessage
        {
            JoinRef = null,
            Ref = NextRef(),
            Topic = _channelTopic!,
            Event = "broadcast",
            Payload = new Dictionary<string, object>
            {
                ["type"] = "broadcast",
                ["event"] = eventName,
                ["payload"] = new Dictionary<string, object>
                {
                    ["data"] = data,
                },
            },
        };
        await SendJsonAsync(msg, ct);
    }

    public async Task SendSignalingAsync(string type, object payload, CancellationToken ct)
    {
        if (!IsConnected) return;

        var msg = new PhoenixMessage
        {
            JoinRef = null,
            Ref = NextRef(),
            Topic = _channelTopic!,
            Event = "broadcast",
            Payload = new Dictionary<string, object>
            {
                ["type"] = "broadcast",
                ["event"] = "signal",
                ["payload"] = new Dictionary<string, object>
                {
                    ["signal_type"] = type,
                    ["data"] = payload,
                },
            },
        };
        await SendJsonAsync(msg, ct);
    }

    private async Task SendJsonAsync(object msg, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            var json = JsonSerializer.Serialize(msg, PhoenixJsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch { }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25), ct);
                var hb = new PhoenixMessage
                {
                    JoinRef = null,
                    Ref = NextRef(),
                    Topic = "phoenix",
                    Event = "heartbeat",
                    Payload = new Dictionary<string, object>(),
                };
                await SendJsonAsync(hb, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 64]; // 64KB buffer

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) continue;

                var json = Encoding.UTF8.GetString(ms.ToArray());
                HandleMessage(json);
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch { }
        }

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void HandleMessage(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<PhoenixMessage>(json, PhoenixJsonOpts);
            if (msg == null) return;

            if (msg.Event == "phx_reply" && msg.Topic == _channelTopic)
            {
                _joined = true;
                _joinTcs?.TrySetResult(true);
                StatusChanged?.Invoke(this, "Realtime channel joined");
                return;
            }

            if (msg.Event == "broadcast" && msg.Topic == _channelTopic)
            {
                // Extract the inner payload
                if (msg.Payload is JsonElement je)
                {
                    var payload = je.GetProperty("payload");
                    var eventName = je.GetProperty("event").GetString();

                    if (eventName is "input" or "signal" or "pc_control" or "camera_control")
                    {
                        TextMessageReceived?.Invoke(this, payload.GetRawText());
                    }
                }
            }
        }
        catch { }
    }

    private string NextRef() => Interlocked.Increment(ref _ref).ToString();

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done",
                    new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
            }
            catch { }
        }
        _ws?.Dispose();
        _ws = null;
        _joined = false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }

    private static readonly JsonSerializerOptions PhoenixJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class PhoenixMessage
{
    [JsonPropertyName("join_ref")] public string? JoinRef { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("topic")] public string Topic { get; set; } = "";
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("payload")] public object? Payload { get; set; }
}
