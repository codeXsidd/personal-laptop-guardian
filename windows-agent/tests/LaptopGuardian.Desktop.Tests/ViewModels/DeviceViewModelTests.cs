using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class DeviceViewModelTests
{
    [Fact]
    public void DefaultState_IsEmpty()
    {
        var statusService = new AgentStatusService();
        var vm = new DeviceViewModel(statusService);

        Assert.Equal("", vm.DeviceId);
        Assert.Equal("", vm.MachineName);
        Assert.Equal("", vm.Hostname);
        Assert.Equal("", vm.OsVersion);
        Assert.False(vm.IsPaired);
        Assert.False(vm.IsOnline);
        Assert.False(vm.IsServiceRunning);
        Assert.Null(vm.PairedAt);
    }

    [Fact]
    public void DefaultState_NotUnpairing()
    {
        var statusService = new AgentStatusService();
        var vm = new DeviceViewModel(statusService);
        Assert.False(vm.IsUnpairing);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void UnpairDeviceCommand_IsNotNull()
    {
        var statusService = new AgentStatusService();
        var vm = new DeviceViewModel(statusService);
        Assert.NotNull(vm.UnpairDeviceCommand);
    }
}
