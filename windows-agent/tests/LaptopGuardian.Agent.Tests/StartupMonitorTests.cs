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

    [Fact]
    public async Task StartAsync_EmitsSystemStartupEvent()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None);

        var events = await _store.GetPendingEventsAsync(100);
        var startupEvent = events.FirstOrDefault(e => e.EventType == EventType.SystemStartup);
        Assert.NotNull(startupEvent);
        Assert.Contains("boot_id", startupEvent.PayloadJson);
        Assert.Contains("boot_time", startupEvent.PayloadJson);
    }

    [Fact]
    public async Task StartAsync_SystemStartupHasStableBootId()
    {
        var monitor1 = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);
        await monitor1.StartAsync(CancellationToken.None);

        var events1 = await _store.GetPendingEventsAsync(100);
        var startup1 = events1.First(e => e.EventType == EventType.SystemStartup);

        var monitor2 = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);
        await monitor2.StartAsync(CancellationToken.None);

        var events2 = await _store.GetPendingEventsAsync(100);
        var startups = events2.Where(e => e.EventType == EventType.SystemStartup).ToList();

        // Both should produce the same ID within the same boot, so INSERT OR IGNORE
        // means we still only have one system_startup event in the store
        Assert.Single(startups);
        Assert.Equal(startup1.Id, startups[0].Id);
    }

    [Fact]
    public async Task StopAsync_EmitsSystemShutdownEvent()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);
        await monitor.StartAsync(CancellationToken.None);

        await monitor.StopAsync(CancellationToken.None);

        var events = await _store.GetPendingEventsAsync(100);
        var shutdownEvent = events.FirstOrDefault(e => e.EventType == EventType.SystemShutdown);
        Assert.NotNull(shutdownEvent);
        Assert.Contains("reason", shutdownEvent.PayloadJson);
        Assert.Contains("service_stopped", shutdownEvent.PayloadJson);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);

        await monitor.StopAsync(CancellationToken.None);

        var events = await _store.GetPendingEventsAsync(100);
        Assert.DoesNotContain(events, e => e.EventType == EventType.SystemShutdown);
    }

    [Fact]
    public async Task StartAsync_SystemStartupAndAgentStarted_AreDifferentEvents()
    {
        var monitor = new StartupMonitor(_store, _identityService, NullLogger<StartupMonitor>.Instance);

        await monitor.StartAsync(CancellationToken.None);

        var events = await _store.GetPendingEventsAsync(100);
        var agentStarted = events.First(e => e.EventType == EventType.AgentStarted);
        var systemStartup = events.First(e => e.EventType == EventType.SystemStartup);

        Assert.NotEqual(agentStarted.Id, systemStartup.Id);
        Assert.NotEqual(agentStarted.EventType, systemStartup.EventType);
    }
}
