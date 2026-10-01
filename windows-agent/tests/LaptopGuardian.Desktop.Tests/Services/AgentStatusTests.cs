using LaptopGuardian.Desktop.Services;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.Services;

public class AgentStatusTests
{
    [Fact]
    public void Disconnected_ProtectionStatus_IsServiceOffline()
    {
        var status = AgentStatus.Disconnected;
        Assert.Equal("Service Offline", status.ProtectionStatus);
        Assert.False(status.IsRunning);
    }

    [Fact]
    public void Running_NotRegistered_IsNotRegistered()
    {
        var status = new AgentStatus { IsRunning = true, IsRegistered = false };
        Assert.Equal("Not Registered", status.ProtectionStatus);
    }

    [Fact]
    public void Running_Registered_NotPaired_IsAwaitingPairing()
    {
        var status = new AgentStatus { IsRunning = true, IsRegistered = true, IsPaired = false };
        Assert.Equal("Awaiting Pairing", status.ProtectionStatus);
    }

    [Fact]
    public void Running_Registered_Paired_Offline_IsOffline()
    {
        var status = new AgentStatus { IsRunning = true, IsRegistered = true, IsPaired = true, IsOnline = false };
        Assert.Equal("Offline", status.ProtectionStatus);
    }

    [Fact]
    public void Running_Registered_Paired_Online_IsProtected()
    {
        var status = new AgentStatus { IsRunning = true, IsRegistered = true, IsPaired = true, IsOnline = true };
        Assert.Equal("Protected", status.ProtectionStatus);
    }
}
