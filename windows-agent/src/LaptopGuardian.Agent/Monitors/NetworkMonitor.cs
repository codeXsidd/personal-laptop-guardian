using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
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
        var wlanInfo = OperatingSystem.IsWindows() ? GetWlanInterfaces() : [];
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

                wlanInfo.TryGetValue(nic.Name, out var wlan);

                adapters.Add(new AdapterInfo(
                    nic.Name,
                    nic.NetworkInterfaceType.ToString(),
                    ipv4,
                    ipv6,
                    nic.OperationalStatus.ToString(),
                    wlan?.Ssid,
                    wlan?.Bssid,
                    wlan?.Signal,
                    wlan?.RadioType,
                    wlan?.Authentication,
                    wlan?.Channel));
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
                if (adapter.Ssid is not null)
                    entry["ssid"] = adapter.Ssid;
                if (adapter.Bssid is not null)
                    entry["bssid"] = adapter.Bssid;
                if (adapter.Signal is not null)
                    entry["signal"] = adapter.Signal;
                if (adapter.RadioType is not null)
                    entry["radio_type"] = adapter.RadioType;
                if (adapter.Authentication is not null)
                    entry["authentication"] = adapter.Authentication;
                if (adapter.Channel is not null)
                    entry["channel"] = adapter.Channel;
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

    [SupportedOSPlatform("windows")]
    internal static Dictionary<string, WlanInfo> GetWlanInterfaces()
    {
        var result = new Dictionary<string, WlanInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            string? currentName = null;
            string? ssid = null;
            string? bssid = null;
            string? signal = null;
            string? radioType = null;
            string? authentication = null;
            string? channel = null;

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    if (currentName is not null && ssid is not null)
                    {
                        result[currentName] = new WlanInfo(ssid, bssid, signal, radioType, authentication, channel);
                    }
                    currentName = line[(line.IndexOf(':') + 1)..].Trim();
                    ssid = bssid = signal = radioType = authentication = channel = null;
                }
                else if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)
                         && !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase)
                         && line.Contains(':'))
                {
                    ssid = line[(line.IndexOf(':') + 1)..].Trim();
                }
                else if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    bssid = line[(line.IndexOf(':') + 1)..].Trim();
                }
                else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    signal = line[(line.IndexOf(':') + 1)..].Trim();
                }
                else if (line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    radioType = line[(line.IndexOf(':') + 1)..].Trim();
                }
                else if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    authentication = line[(line.IndexOf(':') + 1)..].Trim();
                }
                else if (line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                {
                    channel = line[(line.IndexOf(':') + 1)..].Trim();
                }
            }

            if (currentName is not null && ssid is not null)
            {
                result[currentName] = new WlanInfo(ssid, bssid, signal, radioType, authentication, channel);
            }
        }
        catch
        {
            // netsh not available or failed
        }

        return result;
    }

    internal sealed record AdapterInfo(
        string Name,
        string Type,
        string? IPv4,
        string? IPv6,
        string Status,
        string? Ssid = null,
        string? Bssid = null,
        string? Signal = null,
        string? RadioType = null,
        string? Authentication = null,
        string? Channel = null);

    internal sealed record WlanInfo(
        string Ssid,
        string? Bssid,
        string? Signal,
        string? RadioType,
        string? Authentication,
        string? Channel);
}
