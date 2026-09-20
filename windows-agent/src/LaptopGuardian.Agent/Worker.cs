using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.Storage;

namespace LaptopGuardian.Agent;

public sealed class Worker : BackgroundService
{
    private readonly IEventStore _eventStore;
    private readonly IEnumerable<IEventMonitor> _monitors;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IEventStore eventStore,
        IEnumerable<IEventMonitor> monitors,
        ILogger<Worker> logger)
    {
        _eventStore = eventStore;
        _monitors = monitors;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Laptop Guardian agent starting");

        await _eventStore.InitializeAsync(stoppingToken);
        _logger.LogInformation("Event store initialized");

        foreach (var monitor in _monitors)
        {
            try
            {
                _logger.LogInformation("Starting monitor: {MonitorName}", monitor.MonitorName);
                await monitor.StartAsync(stoppingToken);
                _logger.LogInformation("Monitor started: {MonitorName}", monitor.MonitorName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start monitor: {MonitorName}", monitor.MonitorName);
            }
        }

        var pendingCount = await _eventStore.GetPendingCountAsync(stoppingToken);
        _logger.LogInformation("Agent running. Pending events in queue: {PendingCount}", pendingCount);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Laptop Guardian agent stopping");

        foreach (var monitor in _monitors.Reverse())
        {
            try
            {
                _logger.LogInformation("Stopping monitor: {MonitorName}", monitor.MonitorName);
                await monitor.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping monitor: {MonitorName}", monitor.MonitorName);
            }
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Laptop Guardian agent stopped");
    }
}
