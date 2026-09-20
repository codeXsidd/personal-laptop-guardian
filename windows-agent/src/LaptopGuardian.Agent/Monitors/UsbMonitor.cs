using System.Management;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class UsbMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<UsbMonitor> _logger;
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private string? _deviceId;

    public string MonitorName => "USB";

    public UsbMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        ILogger<UsbMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        try
        {
            _insertWatcher = new ManagementEventWatcher(
                new WqlEventQuery(
                    "__InstanceCreationEvent",
                    TimeSpan.FromSeconds(2),
                    "TargetInstance ISA 'Win32_PnPEntity'"));
            _insertWatcher.EventArrived += OnDeviceInserted;
            _insertWatcher.Start();

            _removeWatcher = new ManagementEventWatcher(
                new WqlEventQuery(
                    "__InstanceDeletionEvent",
                    TimeSpan.FromSeconds(2),
                    "TargetInstance ISA 'Win32_PnPEntity'"));
            _removeWatcher.EventArrived += OnDeviceRemoved;
            _removeWatcher.Start();

            _logger.LogInformation("UsbMonitor started — watching for device changes");
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "UsbMonitor: Failed to start WMI watcher — USB monitoring unavailable");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "UsbMonitor: Insufficient permissions for WMI — USB monitoring unavailable");
        }
    }

    private async void OnDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            await HandleDeviceEvent(e, EventType.UsbConnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UsbMonitor: Error processing device insertion");
        }
    }

    private async void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        try
        {
            await HandleDeviceEvent(e, EventType.UsbDisconnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UsbMonitor: Error processing device removal");
        }
    }

    private async Task HandleDeviceEvent(EventArrivedEventArgs e, string eventType)
    {
        if (_deviceId is null)
            return;

        var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
        var deviceInfo = ParseDeviceInfo(targetInstance);

        if (deviceInfo is null)
            return;

        var (name, deviceClass, manufacturer, deviceId) = deviceInfo.Value;

        var payload = new Dictionary<string, object>
        {
            ["device_name"] = name,
            ["device_class"] = deviceClass,
            ["manufacturer"] = manufacturer,
            ["device_id"] = deviceId
        };

        var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
        await _eventStore.InsertEventAsync(deviceEvent);

        _logger.LogDebug("USB event: {EventType} — {DeviceName} ({DeviceClass})",
            eventType, name, deviceClass);
    }

    internal static (string name, string deviceClass, string manufacturer, string deviceId)?
        ParseDeviceInfo(ManagementBaseObject instance)
    {
        var pnpDeviceId = instance["PNPDeviceID"]?.ToString() ?? "";

        if (!IsUsbDevice(pnpDeviceId))
            return null;

        var name = instance["Caption"]?.ToString()
                   ?? instance["Name"]?.ToString()
                   ?? instance["Description"]?.ToString()
                   ?? "Unknown device";

        var deviceClass = instance["PNPClass"]?.ToString() ?? "Unknown";
        var manufacturer = instance["Manufacturer"]?.ToString() ?? "Unknown";

        return (name, deviceClass, manufacturer, pnpDeviceId);
    }

    internal static bool IsUsbDevice(string pnpDeviceId)
    {
        return pnpDeviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
               pnpDeviceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) ||
               pnpDeviceId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase) ||
               pnpDeviceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopWatcher(_insertWatcher);
        StopWatcher(_removeWatcher);
        _logger.LogInformation("UsbMonitor stopped");
        return Task.CompletedTask;
    }

    private static void StopWatcher(ManagementEventWatcher? watcher)
    {
        if (watcher is null) return;
        try
        {
            watcher.Stop();
        }
        catch (ManagementException)
        {
            // Ignore errors during shutdown
        }
    }

    public void Dispose()
    {
        _insertWatcher?.Dispose();
        _removeWatcher?.Dispose();
    }
}
