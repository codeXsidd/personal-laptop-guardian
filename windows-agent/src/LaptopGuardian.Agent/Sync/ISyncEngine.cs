namespace LaptopGuardian.Agent.Sync;

public interface ISyncEngine : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
