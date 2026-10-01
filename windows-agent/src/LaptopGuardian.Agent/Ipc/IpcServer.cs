using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Heartbeat;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.Storage;
using LaptopGuardian.Agent.Backend;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Ipc;

public sealed class IpcServer : IHostedService, IDisposable
{
    public const string PipeName = "LaptopGuardianIPC";

    private readonly IDeviceIdentityService _identityService;
    private readonly IEventStore _eventStore;
    private readonly ISystemMetricsProvider _metricsProvider;
    private readonly IHeartbeatService _heartbeatService;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly IBackendClient _backendClient;
    private readonly AgentOptions _options;
    private readonly ILogger<IpcServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public IpcServer(
        IDeviceIdentityService identityService,
        IEventStore eventStore,
        ISystemMetricsProvider metricsProvider,
        IHeartbeatService heartbeatService,
        IConnectivityTracker connectivityTracker,
        IBackendClient backendClient,
        IOptions<AgentOptions> options,
        ILogger<IpcServer> logger)
    {
        _identityService = identityService;
        _eventStore = eventStore;
        _metricsProvider = metricsProvider;
        _heartbeatService = heartbeatService;
        _connectivityTracker = connectivityTracker;
        _backendClient = backendClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenTask = ListenAsync(_cts.Token);
        _logger.LogInformation("IPC server started on pipe: {PipeName}", PipeName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IPC server stopping");
        _cts?.Cancel();
        if (_listenTask is not null)
        {
            try { await _listenTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipeWithSecurity();

                await pipe.WaitForConnectionAsync(ct);
                _ = HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC listener error");
                await Task.Delay(1000, ct);
            }
        }
    }

    private static NamedPipeServerStream CreatePipeWithSecurity()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            await using (pipe)
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line is null) return;

                var request = JsonSerializer.Deserialize<IpcRequest>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request is null) return;

                var response = await ProcessRequestAsync(request, ct);
                var json = JsonSerializer.Serialize(response,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await writer.WriteLineAsync(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IPC client handler error");
        }
    }

    private async Task<IpcResponse> ProcessRequestAsync(IpcRequest request, CancellationToken ct)
    {
        try
        {
            return request.Command?.ToLowerInvariant() switch
            {
                "status" => await GetStatusAsync(ct),
                "events" => await GetRecentEventsAsync(request, ct),
                "refresh-code" => await RefreshPairingCodeAsync(ct),
                "unpair" => await UnpairDeviceAsync(ct),
                "get-remote-config" => await GetRemoteConfigAsync(ct),
                _ => new IpcResponse { Success = false, Error = $"Unknown command: {request.Command}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPC command error: {Command}", request.Command);
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    private async Task<IpcResponse> GetStatusAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        var metrics = _metricsProvider.LatestMetrics;
        var pendingCount = await _eventStore.GetPendingCountAsync(ct);
        var totalCount = await _eventStore.GetTotalCountAsync(ct);

        var data = new Dictionary<string, object?>
        {
            ["running"] = true,
            ["paired"] = identity.IsPaired,
            ["registered"] = identity.IsRegistered,
            ["device_id"] = identity.ServerDeviceId,
            ["machine_name"] = identity.MachineName,
            ["pairing_code"] = identity.IsPaired ? null : identity.PairingCode,
            ["pairing_code_expires_at"] = identity.IsPaired ? null : identity.PairingCodeExpiresAt?.ToString("o"),
            ["paired_at"] = identity.PairedAt?.ToString("o"),
            ["online"] = _connectivityTracker.IsOnline,
            ["pending_events"] = pendingCount,
            ["total_events"] = totalCount,
            ["cpu_percent"] = metrics?.CpuPercent,
            ["memory_percent"] = metrics?.MemoryPercent,
            ["disk_percent"] = metrics?.Disks.FirstOrDefault()?.UsedPercent,
            ["battery_percent"] = metrics?.BatteryPercent,
            ["battery_status"] = metrics?.BatteryStatus,
            ["hostname"] = metrics?.Hostname ?? identity.MachineName,
            ["os_version"] = metrics?.OsVersion,
            ["metrics_captured_at"] = metrics?.CapturedAt.ToString("o"),
        };

        return new IpcResponse { Success = true, Data = data };
    }

    private async Task<IpcResponse> GetRecentEventsAsync(IpcRequest request, CancellationToken ct)
    {
        var limit = 50;
        if (request.Args?.TryGetValue("limit", out var limitObj) == true)
        {
            if (limitObj is JsonElement je && je.TryGetInt32(out var l)) limit = l;
        }

        var events = await _eventStore.GetPendingEventsAsync(limit, ct);
        var eventList = events.Select(e => new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["event_type"] = e.EventType,
            ["severity"] = e.Severity,
            ["timestamp"] = e.Timestamp.ToString("o"),
            ["payload"] = e.PayloadJson,
        }).ToList();

        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object?> { ["events"] = eventList, ["count"] = eventList.Count }
        };
    }

    private async Task<IpcResponse> RefreshPairingCodeAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        if (!identity.IsRegistered)
        {
            return new IpcResponse { Success = false, Error = "Device not registered with backend" };
        }

        if (identity.IsPaired)
        {
            return new IpcResponse { Success = false, Error = "Device is already paired" };
        }

        try
        {
            var response = await _backendClient.RefreshPairingCodeAsync(identity.ApiKey!, ct);
            identity.PairingCode = response.PairingCode;
            identity.PairingCodeExpiresAt = response.ExpiresAt;
            await _identityService.SaveIdentityAsync(identity, ct);

            return new IpcResponse
            {
                Success = true,
                Data = new Dictionary<string, object?>
                {
                    ["pairing_code"] = response.PairingCode,
                    ["expires_at"] = response.ExpiresAt.ToString("o"),
                }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Error = $"Failed to refresh code: {ex.Message}" };
        }
    }

    private async Task<IpcResponse> UnpairDeviceAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        if (!identity.IsPaired)
        {
            return new IpcResponse { Success = false, Error = "Device is not paired" };
        }

        try
        {
            await _backendClient.UnpairDeviceAsync(identity.ApiKey!, ct);

            identity.PairedAt = null;
            identity.PairingCode = null;
            identity.PairingCodeExpiresAt = null;
            await _identityService.SaveIdentityAsync(identity, ct);

            _logger.LogInformation("Device unpaired via IPC");
            return new IpcResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpair device via IPC");
            return new IpcResponse { Success = false, Error = $"Failed to unpair: {ex.Message}" };
        }
    }

    private async Task<IpcResponse> GetRemoteConfigAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        if (!identity.IsRegistered)
        {
            return new IpcResponse { Success = false, Error = "Device not registered" };
        }

        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object?>
            {
                ["device_id"] = identity.ServerDeviceId,
                ["api_key"] = identity.ApiKey,
                ["supabase_url"] = _options.SupabaseUrl,
            }
        };
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public sealed class IpcRequest
{
    public string? Command { get; set; }
    public Dictionary<string, object>? Args { get; set; }
}

public sealed class IpcResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}
