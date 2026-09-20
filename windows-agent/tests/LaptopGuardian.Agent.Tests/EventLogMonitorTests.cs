using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class EventLogMonitorTests
{
    [Fact]
    public void BuildXPathQuery_ErrorAndCritical_GeneratesCorrectXPath()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = ["Error", "Critical"]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Equal("*[System[(Level=2 or Level=1)]]", xpath);
    }

    [Fact]
    public void BuildXPathQuery_WithEventIds_IncludesEventIdFilter()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "System",
            Levels = ["Error"],
            EventIds = [1000, 2000]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Contains("Level=2", xpath);
        Assert.Contains("EventID=1000", xpath);
        Assert.Contains("EventID=2000", xpath);
    }

    [Fact]
    public void BuildXPathQuery_NoFilters_ReturnsWildcard()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = []
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Equal("*", xpath);
    }

    [Fact]
    public void BuildXPathQuery_OnlyEventIds_NoLevelFilter()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = [],
            EventIds = [100, 200, 300]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Equal("*[System[(EventID=100 or EventID=200 or EventID=300)]]", xpath);
        Assert.DoesNotContain("Level", xpath);
    }

    [Fact]
    public void BuildXPathQuery_AllLevels_IncludesAll()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "System",
            Levels = ["Critical", "Error", "Warning", "Information", "Verbose"]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Contains("Level=1", xpath); // Critical
        Assert.Contains("Level=2", xpath); // Error
        Assert.Contains("Level=3", xpath); // Warning
        Assert.Contains("Level=4", xpath); // Information
        Assert.Contains("Level=5", xpath); // Verbose
    }

    [Fact]
    public void BuildXPathQuery_InvalidLevel_Ignored()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = ["InvalidLevel"]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Equal("*", xpath);
    }

    [Theory]
    [InlineData((byte)1, "Critical")]
    [InlineData((byte)2, "Error")]
    [InlineData((byte)3, "Warning")]
    [InlineData((byte)4, "Information")]
    [InlineData((byte)5, "Verbose")]
    [InlineData((byte)0, "Unknown")]
    [InlineData((byte)99, "Unknown")]
    public void LevelNumberToName_MapsCorrectly(byte level, string expected)
    {
        var result = EventLogMonitor.LevelNumberToName(level);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void LevelNumberToName_Null_ReturnsUnknown()
    {
        var result = EventLogMonitor.LevelNumberToName(null);
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData((byte)1, "critical")]
    [InlineData((byte)2, "high")]
    [InlineData((byte)3, "medium")]
    [InlineData((byte)4, "info")]
    [InlineData((byte)5, "low")]
    [InlineData(null, "info")]
    public void MapLevelToSeverity_MapsCorrectly(byte? level, string expected)
    {
        var result = EventLogMonitor.MapLevelToSeverity(level);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildXPathQuery_CaseInsensitiveLevels()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = ["error", "CRITICAL", "Warning"]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Contains("Level=2", xpath);
        Assert.Contains("Level=1", xpath);
        Assert.Contains("Level=3", xpath);
    }

    [Fact]
    public void BuildXPathQuery_SingleEventId_NoOr()
    {
        var channel = new EventLogChannelConfig
        {
            Name = "Application",
            Levels = [],
            EventIds = [42]
        };

        var xpath = EventLogMonitor.BuildXPathQuery(channel);

        Assert.Equal("*[System[(EventID=42)]]", xpath);
    }
}
