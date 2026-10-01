using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class RemoteAccessViewModelTests
{
    [Fact]
    public void DefaultState_NotActive()
    {
        var service = new RemoteAccessService();
        var vm = new RemoteAccessViewModel(service);

        Assert.False(vm.HasActiveSession);
        Assert.False(vm.HasPendingRequest);
        Assert.Null(vm.PendingSessionId);
        Assert.False(vm.IsProcessing);
    }

    [Fact]
    public void DefaultState_HasStatus()
    {
        var service = new RemoteAccessService();
        var vm = new RemoteAccessViewModel(service);

        Assert.NotNull(vm.Status);
        Assert.NotEmpty(vm.Status);
    }

    [Fact]
    public void Commands_AreNotNull()
    {
        var service = new RemoteAccessService();
        var vm = new RemoteAccessViewModel(service);

        Assert.NotNull(vm.ApproveCommand);
        Assert.NotNull(vm.RejectCommand);
        Assert.NotNull(vm.EndSessionCommand);
    }
}
