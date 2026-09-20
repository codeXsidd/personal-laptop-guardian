using LaptopGuardian.Agent.Models;

namespace LaptopGuardian.Agent.Storage;

public interface IEventStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task InsertEventAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceEvent>> GetPendingEventsAsync(int limit, CancellationToken cancellationToken = default);
    Task MarkSyncedAsync(IEnumerable<string> eventIds, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string eventId, CancellationToken cancellationToken = default);
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
}
