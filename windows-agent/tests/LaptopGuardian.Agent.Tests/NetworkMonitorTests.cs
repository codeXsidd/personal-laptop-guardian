using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class NetworkMonitorTests
{
    private static NetworkMonitor.AdapterInfo CreateAdapter(
        string name = "Ethernet",
        string type = "Ethernet",
        string? ipv4 = "192.168.1.100",
        string? ipv6 = null,
        string status = "Up") =>
        new(name, type, ipv4, ipv6, status);

    [Fact]
    public void DetermineEventType_NullPreviousAndCurrentHasAdapters_ReturnsConnected()
    {
        var current = new List<NetworkMonitor.AdapterInfo> { CreateAdapter() };

        var result = NetworkMonitor.DetermineEventType(null, current);

        Assert.Equal(EventType.NetworkConnected, result);
    }

    [Fact]
    public void DetermineEventType_NullPreviousAndCurrentEmpty_ReturnsDisconnected()
    {
        var result = NetworkMonitor.DetermineEventType(null, []);

        Assert.Equal(EventType.NetworkDisconnected, result);
    }

    [Fact]
    public void DetermineEventType_HadAdaptersNowEmpty_ReturnsDisconnected()
    {
        var previous = new List<NetworkMonitor.AdapterInfo> { CreateAdapter() };

        var result = NetworkMonitor.DetermineEventType(previous, []);

        Assert.Equal(EventType.NetworkDisconnected, result);
    }

    [Fact]
    public void DetermineEventType_HadAdaptersStillHasAdapters_ReturnsChanged()
    {
        var previous = new List<NetworkMonitor.AdapterInfo> { CreateAdapter() };
        var current = new List<NetworkMonitor.AdapterInfo>
        {
            CreateAdapter("WiFi", "Wireless80211", "10.0.0.5")
        };

        var result = NetworkMonitor.DetermineEventType(previous, current);

        Assert.Equal(EventType.NetworkChanged, result);
    }

    [Fact]
    public void DetermineEventType_EmptyPreviousNowHasAdapters_ReturnsConnected()
    {
        var previous = new List<NetworkMonitor.AdapterInfo>();
        var current = new List<NetworkMonitor.AdapterInfo> { CreateAdapter() };

        var result = NetworkMonitor.DetermineEventType(previous, current);

        Assert.Equal(EventType.NetworkConnected, result);
    }

    [Fact]
    public void BuildSnapshotPayload_EmptyAdapters_HasZeroCount()
    {
        var payload = NetworkMonitor.BuildSnapshotPayload([]);

        Assert.Equal(0, payload["adapter_count"]);
        Assert.False(payload.ContainsKey("adapters"));
    }

    [Fact]
    public void BuildSnapshotPayload_WithAdapters_IncludesDetails()
    {
        var adapters = new List<NetworkMonitor.AdapterInfo>
        {
            CreateAdapter("Ethernet", "Ethernet", "192.168.1.100", "2001:db8::1")
        };

        var payload = NetworkMonitor.BuildSnapshotPayload(adapters);

        Assert.Equal(1, payload["adapter_count"]);
        Assert.True(payload.ContainsKey("adapters"));
        var adapterList = (List<Dictionary<string, object>>)payload["adapters"];
        Assert.Single(adapterList);
        Assert.Equal("Ethernet", adapterList[0]["name"]);
        Assert.Equal("192.168.1.100", adapterList[0]["ipv4"]);
        Assert.Equal("2001:db8::1", adapterList[0]["ipv6"]);
    }

    [Fact]
    public void BuildSnapshotPayload_OmitsNullIPs()
    {
        var adapters = new List<NetworkMonitor.AdapterInfo>
        {
            CreateAdapter(ipv4: null, ipv6: null)
        };

        var payload = NetworkMonitor.BuildSnapshotPayload(adapters);

        var adapterList = (List<Dictionary<string, object>>)payload["adapters"];
        Assert.False(adapterList[0].ContainsKey("ipv4"));
        Assert.False(adapterList[0].ContainsKey("ipv6"));
    }

    [Fact]
    public void GetActiveAdapters_ReturnsNonNull()
    {
        var adapters = NetworkMonitor.GetActiveAdapters();
        Assert.NotNull(adapters);
    }

    [Fact]
    public void GetActiveAdapters_ExcludesLoopback()
    {
        var adapters = NetworkMonitor.GetActiveAdapters();
        Assert.DoesNotContain(adapters, a => a.Type == "Loopback");
    }

    [Fact]
    public void BuildSnapshotPayload_MultipleAdapters_AllIncluded()
    {
        var adapters = new List<NetworkMonitor.AdapterInfo>
        {
            CreateAdapter("Ethernet", "Ethernet", "192.168.1.100"),
            CreateAdapter("WiFi", "Wireless80211", "10.0.0.5")
        };

        var payload = NetworkMonitor.BuildSnapshotPayload(adapters);

        Assert.Equal(2, payload["adapter_count"]);
        var adapterList = (List<Dictionary<string, object>>)payload["adapters"];
        Assert.Equal(2, adapterList.Count);
    }
}
