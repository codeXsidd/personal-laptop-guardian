using LaptopGuardian.Agent.Models;

namespace LaptopGuardian.Agent.Identity;

public interface IDeviceIdentityService
{
    Task<DeviceIdentity> GetOrCreateIdentityAsync(CancellationToken cancellationToken = default);
    Task SaveIdentityAsync(DeviceIdentity identity, CancellationToken cancellationToken = default);
}
