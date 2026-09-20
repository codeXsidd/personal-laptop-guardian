using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using LaptopGuardian.Agent.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LaptopGuardian.Agent.Tests;

public sealed class SyncEngineTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqliteEventStore _store;
    private readonly IBackendClient _backendClient;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly IDeviceIdentityService _identityService;
    private readonly AgentOptions _agentOptions;
    private readonly SyncEngine _syncEngine;
    private readonly string _tempDir;

    public SyncEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var connectionString = $"Data Source=SyncTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();
        _store = new SqliteEventStore(connectionString, NullLogger<SqliteEventStore>.Instance);

        _backendClient = Substitute.For<IBackendClient>();
        _connectivityTracker = Substitute.For<IConnectivityTracker>();
        _connectivityTracker.IsOnline.Returns(true);

        _identityService = Substitute.For<IDeviceIdentityService>();
        var identity = new DeviceIdentity
        {
            DeviceId = Guid.NewGuid().ToString(),
            ApiKey = "lg_dk_test_key",
            ServerDeviceId = Guid.NewGuid().ToString()
        };
        _identityService.GetOrCreateIdentityAsync(Arg.Any<CancellationToken>()).Returns(identity);

        _agentOptions = new AgentOptions
        {
            DataDirectory = _tempDir,
            SyncIntervalSeconds = 1,
            SyncBatchSize = 50,
            MaxRetryCount = 3
        };

        _syncEngine = new SyncEngine(
            _store, _backendClient, _connectivityTracker, _identityService,
            Options.Create(_agentOptions), NullLogger<SyncEngine>.Instance);
    }

    public async Task InitializeAsync() => await _store.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _syncEngine.Dispose();
        _store.Dispose();
        _keepAliveConnection.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private async Task InsertTestEvent(string? id = null)
    {
        var evt = DeviceEvent.Create("test-device", EventType.AgentStarted, EventSeverity.Info);
        if (id is not null)
        {
            evt = new DeviceEvent
            {
                Id = id,
                EventType = EventType.AgentStarted,
                Severity = EventSeverity.Info,
                Timestamp = DateTimeOffset.UtcNow,
                PayloadJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            };
        }
        await _store.InsertEventAsync(evt);
    }

    [Fact]
    public async Task SyncBatchAsync_SyncsEventsAndMarksAsSynced()
    {
        await InsertTestEvent("sync-1");
        await InsertTestEvent("sync-2");

        _backendClient.IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>())
            .Returns(new IngestEventsResponse(2, 0, "device-id"));

        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        var pending = await _store.GetPendingCountAsync();
        Assert.Equal(0, pending);
    }

    [Fact]
    public async Task SyncBatchAsync_SkipsWhenNoEvents()
    {
        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        await _backendClient.DidNotReceive()
            .IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncBatchAsync_SkipsWhenNotRegistered()
    {
        var unregisteredIdentity = new DeviceIdentity { DeviceId = "test" };
        _identityService.GetOrCreateIdentityAsync(Arg.Any<CancellationToken>()).Returns(unregisteredIdentity);

        await InsertTestEvent("unreg-1");
        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        await _backendClient.DidNotReceive()
            .IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncBatchAsync_MarksEventsFailedOnBackendError()
    {
        await InsertTestEvent("fail-1");

        _backendClient.IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>())
            .Throws(new BackendException("Server error", 500));

        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        var pending = await _store.GetPendingEventsAsync(100);
        Assert.Empty(pending);

        var total = await _store.GetTotalCountAsync();
        Assert.True(total >= 1);
    }

    [Fact]
    public async Task SyncBatchAsync_HandlesAuthenticationError()
    {
        await InsertTestEvent("auth-fail-1");

        _backendClient.IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>())
            .Throws(new BackendAuthenticationException());

        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        var pending = await _store.GetPendingCountAsync();
        Assert.True(pending >= 1, "Events should remain pending on auth failure, not be marked failed");
    }

    [Fact]
    public void GetBackoffDelay_ReturnsBaseInterval_WhenNoFailures()
    {
        var delay = _syncEngine.GetBackoffDelay();
        Assert.Equal(TimeSpan.FromSeconds(_agentOptions.SyncIntervalSeconds), delay);
    }

    [Fact]
    public async Task GetBackoffDelay_IncreasesAfterFailures()
    {
        await InsertTestEvent("backoff-1");
        _backendClient.IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>())
            .Throws(new BackendAuthenticationException());

        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        var delay = _syncEngine.GetBackoffDelay();
        Assert.True(delay > TimeSpan.FromSeconds(_agentOptions.SyncIntervalSeconds));
    }

    [Fact]
    public async Task SyncBatchAsync_SendsApiKeyInRequest()
    {
        await InsertTestEvent("apikey-test");

        _backendClient.IngestEventsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>())
            .Returns(new IngestEventsResponse(1, 0, "device-id"));

        await _syncEngine.SyncBatchAsync(CancellationToken.None);

        await _backendClient.Received(1)
            .IngestEventsAsync("lg_dk_test_key", Arg.Any<IReadOnlyList<DeviceEvent>>(), Arg.Any<CancellationToken>());
    }
}
