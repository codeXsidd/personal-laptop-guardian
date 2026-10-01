using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LaptopGuardian.Desktop.Services;

public sealed class IpcClient
{
    private const string PipeName = "LaptopGuardianIPC";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<IpcResponse> SendAsync(string command, Dictionary<string, object>? args = null)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(Timeout);
            await pipe.ConnectAsync(cts.Token);

            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            var request = JsonSerializer.Serialize(new { command, args },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await writer.WriteLineAsync(request);

            var response = await reader.ReadLineAsync(cts.Token);
            if (response is null) return IpcResponse.Error("Empty response from agent");

            return JsonSerializer.Deserialize<IpcResponse>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? IpcResponse.Error("Failed to parse response");
        }
        catch (TimeoutException)
        {
            return IpcResponse.Error("Agent not responding (timeout)");
        }
        catch (UnauthorizedAccessException)
        {
            return IpcResponse.Error("Access denied to agent pipe — check service permissions");
        }
        catch (IOException ex) when (ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return IpcResponse.Error("Access denied to agent pipe — check service permissions");
        }
        catch (IOException)
        {
            return IpcResponse.Error("Agent service is not running");
        }
        catch (Exception ex)
        {
            return IpcResponse.Error($"Connection failed: {ex.Message}");
        }
    }
}

public sealed class IpcResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, JsonElement>? Data { get; set; }

    public static IpcResponse Error(string message) => new() { Success = false, ErrorMessage = message };

    public T? Get<T>(string key)
    {
        if (Data is null || !Data.TryGetValue(key, out var element)) return default;
        return element.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public string? GetString(string key)
    {
        if (Data is null || !Data.TryGetValue(key, out var element)) return null;
        return element.ValueKind == JsonValueKind.Null ? null : element.ToString();
    }

    public double? GetDouble(string key)
    {
        if (Data is null || !Data.TryGetValue(key, out var element)) return null;
        if (element.ValueKind == JsonValueKind.Null) return null;
        return element.TryGetDouble(out var d) ? d : null;
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (Data is null || !Data.TryGetValue(key, out var element)) return fallback;
        return element.TryGetInt32(out var i) ? i : fallback;
    }

    public bool GetBool(string key)
    {
        if (Data is null || !Data.TryGetValue(key, out var element)) return false;
        return element.ValueKind == JsonValueKind.True;
    }
}
