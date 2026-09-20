using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Identity;

public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly AgentOptions _options;
    private readonly ICredentialProtector _credentialProtector;
    private readonly ILogger<DeviceIdentityService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DeviceIdentity? _cached;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DeviceIdentityService(
        IOptions<AgentOptions> options,
        ICredentialProtector credentialProtector,
        ILogger<DeviceIdentityService> logger)
    {
        _options = options.Value;
        _credentialProtector = credentialProtector;
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
                _cached = DeserializeIdentity(json);
                _logger.LogInformation("Device ID: {DeviceId}, Registered: {IsRegistered}, Paired: {IsPaired}",
                    _cached.DeviceId, _cached.IsRegistered, _cached.IsPaired);
                return _cached;
            }

            _logger.LogInformation("No existing identity found — generating new device ID");
            var identity = new DeviceIdentity
            {
                DeviceId = Guid.NewGuid().ToString(),
                MachineName = Environment.MachineName
            };

            await PersistIdentityAsync(identity, path, cancellationToken);

            _logger.LogInformation("New device identity created: {DeviceId} at {Path}", identity.DeviceId, path);
            _cached = identity;
            return identity;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveIdentityAsync(DeviceIdentity identity, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var path = _options.IdentityPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await PersistIdentityAsync(identity, path, cancellationToken);
            _cached = identity;
            _logger.LogInformation("Device identity saved. Registered: {IsRegistered}, Paired: {IsPaired}",
                identity.IsRegistered, identity.IsPaired);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistIdentityAsync(DeviceIdentity identity, string path, CancellationToken ct)
    {
        var dto = new PersistedIdentity
        {
            DeviceId = identity.DeviceId,
            ServerDeviceId = identity.ServerDeviceId,
            EncryptedApiKey = identity.ApiKey is not null
                ? Convert.ToBase64String(_credentialProtector.Protect(Encoding.UTF8.GetBytes(identity.ApiKey)))
                : null,
            PairingCode = identity.PairingCode,
            PairingCodeExpiresAt = identity.PairingCodeExpiresAt?.ToString("O"),
            PairedAt = identity.PairedAt?.ToString("O"),
            MachineName = identity.MachineName
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private DeviceIdentity DeserializeIdentity(string json)
    {
        var dto = JsonSerializer.Deserialize<PersistedIdentity>(json)
            ?? throw new InvalidOperationException("Failed to deserialize device identity");

        string? apiKey = null;
        if (dto.EncryptedApiKey is not null)
        {
            try
            {
                var encryptedBytes = Convert.FromBase64String(dto.EncryptedApiKey);
                apiKey = Encoding.UTF8.GetString(_credentialProtector.Unprotect(encryptedBytes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt API key — device may need re-registration");
            }
        }

        return new DeviceIdentity
        {
            DeviceId = dto.DeviceId,
            ServerDeviceId = dto.ServerDeviceId,
            ApiKey = apiKey,
            PairingCode = dto.PairingCode,
            PairingCodeExpiresAt = dto.PairingCodeExpiresAt is not null
                ? DateTimeOffset.Parse(dto.PairingCodeExpiresAt)
                : null,
            PairedAt = dto.PairedAt is not null
                ? DateTimeOffset.Parse(dto.PairedAt)
                : null,
            MachineName = dto.MachineName
        };
    }

    internal sealed class PersistedIdentity
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? ServerDeviceId { get; set; }
        public string? EncryptedApiKey { get; set; }
        public string? PairingCode { get; set; }
        public string? PairingCodeExpiresAt { get; set; }
        public string? PairedAt { get; set; }
        public string MachineName { get; set; } = string.Empty;
    }
}
