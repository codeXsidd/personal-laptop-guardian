using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class MainViewModelTests
{
    private static MainViewModel CreateVm()
    {
        var statusService = new AgentStatusService();
        var eventStore = new EventStoreReader("nonexistent.db");
        var pinService = new PinService();
        var pinSetup = new PinSetupViewModel(pinService);
        var pinLock = new PinLockViewModel(pinService);
        var remoteService = new RemoteAccessService();
        return new MainViewModel(
            new DashboardViewModel(statusService, eventStore),
            new PairPhoneViewModel(statusService),
            new ActivityViewModel(eventStore),
            new DeviceViewModel(statusService),
            new SettingsViewModel(pinSetup),
            pinService,
            pinLock,
            new RemoteAccessViewModel(remoteService));
    }

    [Fact]
    public void InitialView_IsDashboard()
    {
        var vm = CreateVm();
        Assert.IsType<DashboardViewModel>(vm.CurrentView);
        Assert.Equal("Dashboard", vm.SelectedNav);
    }

    [Fact]
    public void Navigate_ChangesCurrentView_ToPairPhone()
    {
        var vm = CreateVm();
        vm.NavigateCommand.Execute("PairPhone");
        Assert.IsType<PairPhoneViewModel>(vm.CurrentView);
        Assert.Equal("PairPhone", vm.SelectedNav);
    }

    [Fact]
    public void Navigate_ChangesCurrentView_ToActivity()
    {
        var vm = CreateVm();
        vm.NavigateCommand.Execute("Activity");
        Assert.IsType<ActivityViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Navigate_ChangesCurrentView_ToDevice()
    {
        var vm = CreateVm();
        vm.NavigateCommand.Execute("Device");
        Assert.IsType<DeviceViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Navigate_ChangesCurrentView_ToSettings()
    {
        var vm = CreateVm();
        vm.NavigateCommand.Execute("Settings");
        Assert.IsType<SettingsViewModel>(vm.CurrentView);
    }

    [Fact]
    public void Navigate_UnknownPage_DefaultsToDashboard()
    {
        var vm = CreateVm();
        vm.NavigateCommand.Execute("Unknown");
        Assert.IsType<DashboardViewModel>(vm.CurrentView);
    }
}
