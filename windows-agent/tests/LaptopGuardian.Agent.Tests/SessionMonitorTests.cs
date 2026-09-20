using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class SessionMonitorTests
{
    [Theory]
    [InlineData("SYSTEM", true)]
    [InlineData("LOCAL SERVICE", true)]
    [InlineData("NETWORK SERVICE", true)]
    [InlineData("ANONYMOUS LOGON", true)]
    [InlineData("DWM-1", true)]
    [InlineData("UMFD-0", true)]
    [InlineData("MYPC$", true)]
    [InlineData("john", false)]
    [InlineData("Administrator", false)]
    [InlineData("e2e.test", false)]
    public void IsSystemAccount_ClassifiesCorrectly(string username, bool expected)
    {
        Assert.Equal(expected, SessionMonitor.IsSystemAccount(username));
    }

    [Theory]
    [InlineData("", "john", "john")]
    [InlineData("DOMAIN", "john", "DOMAIN\\john")]
    [InlineData("  ", "admin", "admin")]
    public void FormatUsername_FormatsCorrectly(string domain, string username, string expected)
    {
        Assert.Equal(expected, SessionMonitor.FormatUsername(domain, username));
    }

    [Fact]
    public void IsSystemAccount_IsCaseInsensitive()
    {
        Assert.True(SessionMonitor.IsSystemAccount("system"));
        Assert.True(SessionMonitor.IsSystemAccount("System"));
        Assert.True(SessionMonitor.IsSystemAccount("local service"));
    }

    [Fact]
    public void IsSystemAccount_RejectsMachineAccounts()
    {
        Assert.True(SessionMonitor.IsSystemAccount("WORKSTATION$"));
        Assert.True(SessionMonitor.IsSystemAccount("DC01$"));
    }

    [Fact]
    public void IsSystemAccount_AllowsNormalUsers()
    {
        Assert.False(SessionMonitor.IsSystemAccount("alice"));
        Assert.False(SessionMonitor.IsSystemAccount("bob.smith"));
        Assert.False(SessionMonitor.IsSystemAccount("Admin"));
    }
}
