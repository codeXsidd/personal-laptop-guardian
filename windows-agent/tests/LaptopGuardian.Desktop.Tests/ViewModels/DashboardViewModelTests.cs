using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class DashboardViewModelTests
{
    [Fact]
    public void DefaultState_ShowsConnecting()
    {
        var statusService = new AgentStatusService();
        var eventStore = new EventStoreReader("nonexistent.db");
        var vm = new DashboardViewModel(statusService, eventStore);

        Assert.Equal("Connecting...", vm.ProtectionStatus);
        Assert.False(vm.IsServiceRunning);
        Assert.False(vm.IsPaired);
        Assert.False(vm.IsOnline);
        Assert.Equal(0, vm.CpuPercent);
        Assert.Equal(0, vm.MemoryPercent);
        Assert.Equal(0, vm.DiskPercent);
        Assert.Equal(0, vm.BatteryPercent);
        Assert.Empty(vm.RecentEvents);
    }
}
