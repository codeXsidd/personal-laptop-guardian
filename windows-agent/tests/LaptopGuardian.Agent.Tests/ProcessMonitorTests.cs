using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class ProcessMonitorTests
{
    private static ProcessMonitor.ProcessSnapshot CreateSnapshot(int pid, string name,
        DateTimeOffset? startTime = null, string? path = null) =>
        new(pid, name, path, startTime ?? DateTimeOffset.UtcNow, 1);

    [Fact]
    public void CompareSnapshots_DetectsNewProcesses()
    {
        var t = DateTimeOffset.UtcNow;
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t)
        };
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t),
            [new(200, t)] = CreateSnapshot(200, "chrome", t)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Single(started);
        Assert.Equal("chrome", started[0].Name);
        Assert.Empty(stopped);
    }

    [Fact]
    public void CompareSnapshots_DetectsStoppedProcesses()
    {
        var t = DateTimeOffset.UtcNow;
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t),
            [new(200, t)] = CreateSnapshot(200, "chrome", t)
        };
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Empty(started);
        Assert.Single(stopped);
        Assert.Equal("chrome", stopped[0].Name);
    }

    [Fact]
    public void CompareSnapshots_DetectsStartedAndStoppedSimultaneously()
    {
        var t = DateTimeOffset.UtcNow;
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t)
        };
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(200, t)] = CreateSnapshot(200, "code", t)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Single(started);
        Assert.Equal("code", started[0].Name);
        Assert.Single(stopped);
        Assert.Equal("notepad", stopped[0].Name);
    }

    [Fact]
    public void CompareSnapshots_EmptyPreviousReportsAllAsStarted()
    {
        var t = DateTimeOffset.UtcNow;
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>();
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Single(started);
        Assert.Empty(stopped);
    }

    [Fact]
    public void CompareSnapshots_EmptyBothReportsNothing()
    {
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>();
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>();

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Empty(started);
        Assert.Empty(stopped);
    }

    [Fact]
    public void CompareSnapshots_SamePidDifferentStartTime_TreatedAsNewProcess()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddMinutes(5);

        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t1)] = CreateSnapshot(100, "notepad", t1)
        };
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t2)] = CreateSnapshot(100, "calc", t2)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Single(started);
        Assert.Equal("calc", started[0].Name);
        Assert.Single(stopped);
        Assert.Equal("notepad", stopped[0].Name);
    }

    [Fact]
    public void CompareSnapshots_NoChanges_ReportsNothing()
    {
        var t = DateTimeOffset.UtcNow;
        var previous = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t),
            [new(200, t)] = CreateSnapshot(200, "chrome", t)
        };
        var current = new Dictionary<ProcessMonitor.ProcessKey, ProcessMonitor.ProcessSnapshot>
        {
            [new(100, t)] = CreateSnapshot(100, "notepad", t),
            [new(200, t)] = CreateSnapshot(200, "chrome", t)
        };

        var (started, stopped) = ProcessMonitor.CompareSnapshots(previous, current);

        Assert.Empty(started);
        Assert.Empty(stopped);
    }

    [Fact]
    public void TakeSnapshot_ReturnsNonEmptyOnRunningSystem()
    {
        var snapshot = ProcessMonitor.TakeSnapshot();

        Assert.NotEmpty(snapshot);
    }

    [Fact]
    public void TakeSnapshot_ExcludesSystemProcesses()
    {
        var snapshot = ProcessMonitor.TakeSnapshot();

        Assert.DoesNotContain(snapshot, kvp => kvp.Value.Pid is 0 or 4);
        Assert.DoesNotContain(snapshot, kvp =>
            string.Equals(kvp.Value.Name, "Idle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessKey_SamePidAndStartTime_AreEqual()
    {
        var t = DateTimeOffset.UtcNow;
        var key1 = new ProcessMonitor.ProcessKey(100, t);
        var key2 = new ProcessMonitor.ProcessKey(100, t);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ProcessKey_DifferentStartTime_AreNotEqual()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(1);
        var key1 = new ProcessMonitor.ProcessKey(100, t1);
        var key2 = new ProcessMonitor.ProcessKey(100, t2);

        Assert.NotEqual(key1, key2);
    }
}
