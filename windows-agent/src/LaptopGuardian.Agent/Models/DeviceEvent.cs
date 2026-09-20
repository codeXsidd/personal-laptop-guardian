using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LaptopGuardian.Agent.Models;

public sealed class DeviceEvent
{
    public required string Id { get; init; }
    public required string EventType { get; init; }
    public required string Severity { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string PayloadJson { get; init; }
    public string SyncStatus { get; set; } = Models.SyncStatus.Pending;
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }

    public static string GenerateDeterministicId(
        string deviceId, string eventType, DateTimeOffset timestamp, string payloadJson)
    {
        var input = $"{deviceId}|{eventType}|{timestamp:O}|{payloadJson}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // Use first 16 bytes as a UUID v4-like identifier
        hash[6] = (byte)((hash[6] & 0x0F) | 0x40); // version 4
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant 1
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    public static DeviceEvent Create(
        string deviceId, string eventType, string severity, Dictionary<string, object>? payload = null)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payloadJson = payload is not null
            ? JsonSerializer.Serialize(payload)
            : "{}";
        var id = GenerateDeterministicId(deviceId, eventType, timestamp, payloadJson);

        return new DeviceEvent
        {
            Id = id,
            EventType = eventType,
            Severity = severity,
            Timestamp = timestamp,
            PayloadJson = payloadJson
        };
    }
}
