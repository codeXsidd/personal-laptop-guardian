namespace LaptopGuardian.Agent.Backend;

public sealed record RegisterDeviceResponse(
    string DeviceId,
    string ApiKey,
    string PairingCode,
    DateTimeOffset ExpiresAt);

public sealed record IngestEventsResponse(
    int Inserted,
    int Duplicates,
    string DeviceId);

public sealed record HeartbeatResponse(
    string Status,
    string DeviceId,
    DateTimeOffset ServerTime);
