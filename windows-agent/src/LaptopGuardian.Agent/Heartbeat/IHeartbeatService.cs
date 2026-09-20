namespace LaptopGuardian.Agent.Heartbeat;

public interface IHeartbeatService : IDisposable
{
    bool IsPaired { get; }
    event EventHandler? DevicePaired;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
