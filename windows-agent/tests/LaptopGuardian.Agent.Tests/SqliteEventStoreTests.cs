using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LaptopGuardian.Agent.Tests;

public sealed class SqliteEventStoreTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqliteEventStore _store;

    public SqliteEventStoreTests()
    {
        // In-memory database shared via named connection
        var connectionString = "Data Source=TestDb;Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();
        _store = new SqliteEventStore(connectionString, NullLogger<SqliteEventStore>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _store.Dispose();
        _keepAliveConnection.Dispose();
    }

    private static DeviceEvent CreateTestEvent(
        string? id = null,
        string eventType = "agent_started",
        string severity = "info",
        string syncStatus = "pending")
    {
        var ts = DateTimeOffset.UtcNow;
        return new DeviceEvent
        {
            Id = id ?? Guid.NewGuid().ToString(),
            EventType = eventType,
            Severity = severity,
            Timestamp = ts,
            PayloadJson = """{"test": true}""",
            SyncStatus = syncStatus,
            CreatedAt = ts
        };
    }

    [Fact]
    public async Task InitializeAsync_CreatesEventsTable()
    {
        await using var connection = new SqliteConnection("Data Source=TestDb;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='events'";
        var result = await command.ExecuteScalarAsync();

        Assert.Equal("events", result);
    }

    [Fact]
    public async Task InsertEventAsync_InsertsEvent()
    {
        var evt = CreateTestEvent();

        await _store.InsertEventAsync(evt);

        var total = await _store.GetTotalCountAsync();
        Assert.True(total >= 1);
    }

    [Fact]
    public async Task InsertEventAsync_IgnoresDuplicateId()
    {
        var evt = CreateTestEvent(id: "dup-test-id");

        await _store.InsertEventAsync(evt);
        await _store.InsertEventAsync(evt);

        // Count should still be just the ones from this test
        await using var connection = new SqliteConnection("Data Source=TestDb;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE id = 'dup-test-id'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetPendingEventsAsync_ReturnsPendingEvents()
    {
        var pending = CreateTestEvent(id: "pending-1");
        await _store.InsertEventAsync(pending);

        var results = await _store.GetPendingEventsAsync(100);

        Assert.Contains(results, e => e.Id == "pending-1");
        Assert.All(results, e => Assert.Equal(SyncStatus.Pending, e.SyncStatus));
    }

    [Fact]
    public async Task GetPendingEventsAsync_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
        {
            await _store.InsertEventAsync(CreateTestEvent(id: $"limit-test-{i}"));
        }

        var results = await _store.GetPendingEventsAsync(2);

        Assert.True(results.Count <= 2);
    }

    [Fact]
    public async Task MarkSyncedAsync_UpdatesSyncStatus()
    {
        var evt = CreateTestEvent(id: "sync-test-1");
        await _store.InsertEventAsync(evt);

        await _store.MarkSyncedAsync(["sync-test-1"]);

        var pending = await _store.GetPendingEventsAsync(100);
        Assert.DoesNotContain(pending, e => e.Id == "sync-test-1");
    }

    [Fact]
    public async Task MarkFailedAsync_IncrementsRetryCount()
    {
        var evt = CreateTestEvent(id: "fail-test-1");
        await _store.InsertEventAsync(evt);

        await _store.MarkFailedAsync("fail-test-1");
        await _store.MarkFailedAsync("fail-test-1");

        await using var connection = new SqliteConnection("Data Source=TestDb;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT retry_count, sync_status FROM events WHERE id = 'fail-test-1'";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal("failed", reader.GetString(1));
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        var uniqueId = Guid.NewGuid().ToString();
        await _store.InsertEventAsync(CreateTestEvent(id: $"count-{uniqueId}-1"));
        await _store.InsertEventAsync(CreateTestEvent(id: $"count-{uniqueId}-2"));

        var count = await _store.GetPendingCountAsync();

        Assert.True(count >= 2);
    }

    [Fact]
    public async Task GetTotalCountAsync_IncludesAllStatuses()
    {
        var evt = CreateTestEvent(id: "total-count-test");
        await _store.InsertEventAsync(evt);
        await _store.MarkSyncedAsync(["total-count-test"]);

        var total = await _store.GetTotalCountAsync();
        var pending = await _store.GetPendingCountAsync();

        Assert.True(total >= pending);
    }
}
