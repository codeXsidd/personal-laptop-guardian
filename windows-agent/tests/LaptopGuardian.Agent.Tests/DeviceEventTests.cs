using LaptopGuardian.Agent.Models;

namespace LaptopGuardian.Agent.Tests;

public sealed class DeviceEventTests
{
    [Fact]
    public void GenerateDeterministicId_ReturnsSameId_ForSameInput()
    {
        var deviceId = "test-device";
        var eventType = "agent_started";
        var timestamp = DateTimeOffset.Parse("2026-09-20T10:00:00Z");
        var payload = """{"key": "value"}""";

        var id1 = DeviceEvent.GenerateDeterministicId(deviceId, eventType, timestamp, payload);
        var id2 = DeviceEvent.GenerateDeterministicId(deviceId, eventType, timestamp, payload);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void GenerateDeterministicId_ReturnsDifferentId_ForDifferentInput()
    {
        var timestamp = DateTimeOffset.Parse("2026-09-20T10:00:00Z");
        var payload = "{}";

        var id1 = DeviceEvent.GenerateDeterministicId("device-1", "agent_started", timestamp, payload);
        var id2 = DeviceEvent.GenerateDeterministicId("device-2", "agent_started", timestamp, payload);

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void GenerateDeterministicId_ReturnsValidGuid()
    {
        var id = DeviceEvent.GenerateDeterministicId("dev", "type", DateTimeOffset.UtcNow, "{}");

        Assert.True(Guid.TryParse(id, out _));
    }

    [Fact]
    public void Create_SetsAllProperties()
    {
        var payload = new Dictionary<string, object> { ["key"] = "value" };

        var evt = DeviceEvent.Create("test-device", EventType.AgentStarted, EventSeverity.Info, payload);

        Assert.NotNull(evt.Id);
        Assert.Equal(EventType.AgentStarted, evt.EventType);
        Assert.Equal(EventSeverity.Info, evt.Severity);
        Assert.Contains("key", evt.PayloadJson);
        Assert.Equal(SyncStatus.Pending, evt.SyncStatus);
        Assert.Equal(0, evt.RetryCount);
        Assert.Null(evt.SyncedAt);
    }

    [Fact]
    public void Create_WithNullPayload_DefaultsToEmptyJson()
    {
        var evt = DeviceEvent.Create("test-device", EventType.AgentStarted, EventSeverity.Info);

        Assert.Equal("{}", evt.PayloadJson);
    }
}
