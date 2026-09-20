namespace LaptopGuardian.Agent.Monitors;

public interface IEventMonitor : IDisposable
{
    string MonitorName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
