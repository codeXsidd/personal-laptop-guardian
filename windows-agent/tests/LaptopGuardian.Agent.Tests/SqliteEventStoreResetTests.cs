using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LaptopGuardian.Agent.Tests;

public sealed class SqliteEventStoreResetTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqliteEventStore _store;

    public SqliteEventStoreResetTests()
    {
        var connectionString = $"Data Source=ResetTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();
        _store = new SqliteEventStore(connectionString, NullLogger<SqliteEventStore>.Instance);
    }

    public async Task InitializeAsync() => await _store.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _store.Dispose();
        _keepAliveConnection.Dispose();
    }

    private static DeviceEvent CreateTestEvent(string? id = null, string syncStatus = "pending")
    {
        var ts = DateTimeOffset.UtcNow;
        return new DeviceEvent
        {
            Id = id ?? Guid.NewGuid().ToString(),
            EventType = "agent_started",
            Severity = "info",
            Timestamp = ts,
            PayloadJson = """{"test": true}""",
            SyncStatus = syncStatus,
            CreatedAt = ts
        };
    }

    [Fact]
    public async Task ResetFailedEventsAsync_ResetsFailedToPending()
    {
        var evt = CreateTestEvent(id: "reset-test-1");
        await _store.InsertEventAsync(evt);
        await _store.MarkFailedAsync("reset-test-1");

        await _store.ResetFailedEventsAsync(maxRetryCount: 5);

        var pending = await _store.GetPendingEventsAsync(100);
        Assert.Contains(pending, e => e.Id == "reset-test-1");
    }

    [Fact]
    public async Task ResetFailedEventsAsync_DoesNotResetOverMaxRetry()
    {
        var evt = CreateTestEvent(id: "maxretry-test");
        await _store.InsertEventAsync(evt);

        for (int i = 0; i < 3; i++)
            await _store.MarkFailedAsync("maxretry-test");

        await _store.ResetFailedEventsAsync(maxRetryCount: 3);

        var pending = await _store.GetPendingEventsAsync(100);
        Assert.DoesNotContain(pending, e => e.Id == "maxretry-test");
    }

    [Fact]
    public async Task ResetFailedEventsAsync_LeavesSuccessfulEventsAlone()
    {
        var evt = CreateTestEvent(id: "synced-event");
        await _store.InsertEventAsync(evt);
        await _store.MarkSyncedAsync(["synced-event"]);

        await _store.ResetFailedEventsAsync(maxRetryCount: 10);

        var pending = await _store.GetPendingEventsAsync(100);
        Assert.DoesNotContain(pending, e => e.Id == "synced-event");
    }
}
