using LaptopGuardian.Desktop.Services;
using LaptopGuardian.Desktop.ViewModels;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.ViewModels;

public class ActivityViewModelTests
{
    [Fact]
    public void DefaultState_HasFilterOptions()
    {
        var eventStore = new EventStoreReader("nonexistent.db");
        var vm = new ActivityViewModel(eventStore);

        Assert.Equal("All", vm.SelectedFilter);
        Assert.Contains("All", vm.FilterOptions);
        Assert.Contains("Process", vm.FilterOptions);
        Assert.Contains("USB", vm.FilterOptions);
        Assert.Contains("Network", vm.FilterOptions);
    }

    [Fact]
    public async Task LoadEvents_WithMissingDb_ReturnsEmpty()
    {
        var eventStore = new EventStoreReader("nonexistent.db");
        var vm = new ActivityViewModel(eventStore);

        await vm.LoadEventsAsync();
        Assert.Empty(vm.Events);
    }
}
