namespace LaptopGuardian.Agent.Connectivity;

public interface IConnectivityTracker : IDisposable
{
    bool IsOnline { get; }
    event EventHandler<bool>? ConnectivityChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
