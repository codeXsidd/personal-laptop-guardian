using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateVm()
    {
        var pinService = new PinService();
        var pinSetup = new PinSetupViewModel(pinService);
        return new SettingsViewModel(pinSetup);
    }

    [Fact]
    public void DefaultState_HasDefaults()
    {
        var vm = CreateVm();

        Assert.True(vm.MinimizeToTray);
        Assert.False(vm.LaunchOnStartup);
        Assert.True(vm.ShowNotifications);
        Assert.Equal("", vm.StatusMessage);
    }

    [Fact]
    public void SaveCommand_IsNotNull()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.SaveCommand);
    }

    [Fact]
    public void ServiceControlCommands_AreNotNull()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.StartServiceCommand);
        Assert.NotNull(vm.StopServiceCommand);
        Assert.NotNull(vm.RestartServiceCommand);
        Assert.NotNull(vm.RefreshServiceStatusCommand);
        Assert.NotNull(vm.OpenServicesPanelCommand);
    }

    [Fact]
    public void DefaultState_ServiceStatus_NotEmpty()
    {
        var vm = CreateVm();
        Assert.NotEmpty(vm.ServiceStatus);
    }

    [Fact]
    public void DefaultState_NotRunningServiceAction()
    {
        var vm = CreateVm();
        Assert.False(vm.IsServiceActionRunning);
    }

    [Fact]
    public void PinSetup_IsNotNull()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.PinSetup);
    }
}
