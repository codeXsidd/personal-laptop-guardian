using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Tests;

public sealed class StartupMonitorTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqliteEventStore _store;
    private readonly DeviceIdentityService _identityService;

    public StartupMonitorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LaptopGuardianTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var connectionString = $"Data Source=StartupTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        _store = new SqliteEventStore(connectionString, NullLogger<SqliteEventStore>.Instance);
        _identityService = new DeviceIdentityService(
            Options.Create(new AgentOptions { DataDirectory = _tempDir }),
            new PassthroughCredentialProtector(),
            NullLogger<DeviceIdentityService>.Instance);
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
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StartAsync_RecordsAgentStartedEvent()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None);

        var events = await _store.GetPendingEventsAsync(100);
        var startEvent = events.FirstOrDefault(e => e.EventType == EventType.AgentStarted);
        Assert.NotNull(startEvent);
        Assert.Equal(EventSeverity.Info, startEvent.Severity);
        Assert.Contains("machine_name", startEvent.PayloadJson);
        Assert.Contains("agent_version", startEvent.PayloadJson);
    }

    [Fact]
    public async Task StartAsync_UsesDeviceIdentity()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None);

        var identity = await _identityService.GetOrCreateIdentityAsync();
        var events = await _store.GetPendingEventsAsync(100);
        var startEvent = events.First(e => e.EventType == EventType.AgentStarted);
        Assert.Contains(identity.MachineName, startEvent.PayloadJson);
    }
}
