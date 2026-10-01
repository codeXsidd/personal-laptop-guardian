using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class PairPhoneViewModelTests
{
    [Fact]
    public void DefaultState_NotPaired_NoCode()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);

        Assert.False(vm.IsPaired);
        Assert.False(vm.IsServiceRunning);
        Assert.Equal("", vm.PairingCode);
        Assert.False(vm.IsGenerating);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.CodeExpired);
    }

    [Fact]
    public void CopyCodeCommand_IsNotNull()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);
        Assert.NotNull(vm.CopyCodeCommand);
    }

    [Fact]
    public void GenerateNewCodeCommand_IsNotNull()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);
        Assert.NotNull(vm.GenerateNewCodeCommand);
    }

    [Fact]
    public void ShowQrCodeCommand_IsNotNull()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);
        Assert.NotNull(vm.ShowQrCodeCommand);
    }

    [Fact]
    public void UnpairDeviceCommand_IsNotNull()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);
        Assert.NotNull(vm.UnpairDeviceCommand);
    }

    [Fact]
    public void DefaultState_NotUnpairing()
    {
        var statusService = new AgentStatusService();
        var vm = new PairPhoneViewModel(statusService);
        Assert.False(vm.IsUnpairing);
    }
}
