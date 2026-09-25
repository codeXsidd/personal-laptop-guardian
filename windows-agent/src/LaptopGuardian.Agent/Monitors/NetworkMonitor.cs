using System.Net.NetworkInformation;
using System.Net.Sockets;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Monitors;

public sealed class NetworkMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<NetworkMonitor> _logger;
    private string? _deviceId;
    private Timer? _debounceTimer;
    private readonly object _debounceLock = new();
    private List<AdapterInfo>? _lastAdapters;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    public string MonitorName => "Network";

    public NetworkMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        ILogger<NetworkMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        _lastAdapters = GetActiveAdapters();

        var payload = BuildSnapshotPayload(_lastAdapters);
        var initialEvent = DeviceEvent.Create(
            _deviceId, EventType.NetworkConnected, EventSeverity.Info, payload);
        await _eventStore.InsertEventAsync(initialEvent, cancellationToken);

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;

        _logger.LogInformation("NetworkMonitor started — tracking {Count} active adapters",
            _lastAdapters.Count);
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        ScheduleDebounce();
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        ScheduleDebounce();
    }

    private void ScheduleDebounce()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnDebounceElapsed(object? state)
    {
        try
        {
            await ProcessNetworkChangeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NetworkMonitor: Error processing network change");
        }
    }

    internal async Task ProcessNetworkChangeAsync()
    {
        if (_deviceId is null)
            return;

        var currentAdapters = GetActiveAdapters();
        var eventType = DetermineEventType(_lastAdapters, currentAdapters);

        var payload = BuildSnapshotPayload(currentAdapters);
        payload["hostname"] = Environment.MachineName;

        var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
        await _eventStore.InsertEventAsync(deviceEvent);

        _logger.LogDebug("Network event: {EventType} — {Count} active adapters",
            eventType, currentAdapters.Count);

        _lastAdapters = currentAdapters;
    }

    internal static string DetermineEventType(
        List<AdapterInfo>? previous, List<AdapterInfo> current)
    {
        if (previous is null || previous.Count == 0)
            return current.Count > 0 ? EventType.NetworkConnected : EventType.NetworkDisconnected;

        if (current.Count == 0)
            return EventType.NetworkDisconnected;

        if (previous.Count == 0)
            return EventType.NetworkConnected;

        return EventType.NetworkChanged;
    }

    internal static List<AdapterInfo> GetActiveAdapters()
    {
        var adapters = new List<AdapterInfo>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                    continue;

                string? ipv4 = null;
                string? ipv6 = null;

                try
                {
                    var ipProps = nic.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            ipv4 ??= addr.Address.ToString();
                        else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6
                                 && !addr.Address.IsIPv6LinkLocal)
                            ipv6 ??= addr.Address.ToString();
                    }
                }
                catch (NetworkInformationException)
                {
                    // Some adapters don't support IP properties
                }

                adapters.Add(new AdapterInfo(
                    nic.Name,
                    nic.NetworkInterfaceType.ToString(),
                    ipv4,
                    ipv6,
                    nic.OperationalStatus.ToString()));
            }
        }
        catch (NetworkInformationException)
        {
            // Platform doesn't support enumeration — return empty
        }

        return adapters;
    }

    internal static Dictionary<string, object> BuildSnapshotPayload(List<AdapterInfo> adapters)
    {
        var payload = new Dictionary<string, object>
        {
            ["adapter_count"] = adapters.Count
        };

        if (adapters.Count > 0)
        {
            var adapterList = new List<Dictionary<string, object>>();
            foreach (var adapter in adapters)
            {
                var entry = new Dictionary<string, object>
                {
                    ["name"] = adapter.Name,
                    ["type"] = adapter.Type,
                    ["status"] = adapter.Status
                };
                if (adapter.IPv4 is not null)
                    entry["ipv4"] = adapter.IPv4;
                if (adapter.IPv6 is not null)
                    entry["ipv6"] = adapter.IPv6;
                adapterList.Add(entry);
            }

            payload["adapters"] = adapterList;
        }

        return payload;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;

        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _logger.LogInformation("NetworkMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
        }
    }

    internal sealed record AdapterInfo(
        string Name,
        string Type,
        string? IPv4,
        string? IPv6,
        string Status);
}
