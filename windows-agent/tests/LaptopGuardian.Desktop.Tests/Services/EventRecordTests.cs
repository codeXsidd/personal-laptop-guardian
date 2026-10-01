using LaptopGuardian.Desktop.Services;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.Services;

public class EventRecordTests
{
    private static EventRecord Create(string eventType = "session_login", string severity = "low") => new()
    {
        Id = "test-id",
        EventType = eventType,
        Severity = severity,
        Timestamp = DateTimeOffset.UtcNow,
        PayloadJson = "{}",
        SyncStatus = "synced",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("session_login", "Login")]
    [InlineData("session_logout", "Logout")]
    [InlineData("session_lock", "Screen Lock")]
    [InlineData("session_unlock", "Screen Unlock")]
    [InlineData("process_start", "App Started")]
    [InlineData("usb_connected", "USB Connected")]
    [InlineData("network_connected", "Network Connected")]
    [InlineData("agent_started", "Agent Started")]
    [InlineData("unknown_event", "unknown event")]
    public void DisplayName_MapsCorrectly(string type, string expected)
    {
        var e = Create(type);
        Assert.Equal(expected, e.DisplayName);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    [InlineData("info")]
    public void SeverityIcon_IsNotEmpty(string severity)
    {
        var e = Create(severity: severity);
        Assert.NotEmpty(e.SeverityIcon);
    }

    [Fact]
    public void TimeAgo_JustNow()
    {
        var e = Create();
        Assert.Equal("Just now", e.TimeAgo);
    }

    [Fact]
    public void TimeAgo_MinutesAgo()
    {
        var e = new EventRecord
        {
            Id = "test",
            EventType = "test",
            Severity = "low",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            PayloadJson = "{}",
            SyncStatus = "synced",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Assert.Contains("m ago", e.TimeAgo);
    }

    [Fact]
    public void TimeAgo_HoursAgo()
    {
        var e = new EventRecord
        {
            Id = "test",
            EventType = "test",
            Severity = "low",
            Timestamp = DateTimeOffset.UtcNow.AddHours(-3),
            PayloadJson = "{}",
            SyncStatus = "synced",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Assert.Contains("h ago", e.TimeAgo);
    }

    [Fact]
    public void TimeAgo_DaysAgo()
    {
        var e = new EventRecord
        {
            Id = "test",
            EventType = "test",
            Severity = "low",
            Timestamp = DateTimeOffset.UtcNow.AddDays(-2),
            PayloadJson = "{}",
            SyncStatus = "synced",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Assert.Contains("d ago", e.TimeAgo);
    }
}
