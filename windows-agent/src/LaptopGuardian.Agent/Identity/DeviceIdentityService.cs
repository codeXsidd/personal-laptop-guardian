using System.Text.Json;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Identity;

public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly AgentOptions _options;
    private readonly ILogger<DeviceIdentityService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DeviceIdentity? _cached;

    public DeviceIdentityService(IOptions<AgentOptions> options, ILogger<DeviceIdentityService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DeviceIdentity> GetOrCreateIdentityAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
                return _cached;

            var path = _options.IdentityPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                _logger.LogInformation("Loading device identity from {Path}", path);
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                _cached = JsonSerializer.Deserialize<DeviceIdentity>(json)
                    ?? throw new InvalidOperationException("Failed to deserialize device identity");
                _logger.LogInformation("Device ID: {DeviceId}", _cached.DeviceId);
                return _cached;
            }

            _logger.LogInformation("No existing identity found — generating new device ID");
            var identity = new DeviceIdentity
            {
                DeviceId = Guid.NewGuid().ToString(),
                MachineName = Environment.MachineName
            };

            var serialized = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, serialized, cancellationToken);

            _logger.LogInformation("New device identity created: {DeviceId} at {Path}", identity.DeviceId, path);
            _cached = identity;
            return identity;
        }
        finally
        {
            _lock.Release();
        }
    }
}
