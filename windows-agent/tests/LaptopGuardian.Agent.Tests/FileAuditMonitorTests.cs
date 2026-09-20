using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Monitors;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Tests;

public sealed class FileAuditMonitorTests
{
    [Theory]
    [InlineData("0x1", "read_data")]
    [InlineData("0x2", "write_data")]
    [InlineData("0x4", "append_data")]
    [InlineData("0x20", "execute")]
    [InlineData("0x80", "read_attributes")]
    [InlineData("0x100", "write_attributes")]
    [InlineData("0x10000", "delete")]
    [InlineData("0x20000", "read_permissions")]
    [InlineData("0x40000", "write_permissions")]
    public void NormalizeAccessType_KnownMask_ReturnsExpected(string mask, string expected)
    {
        var result = FileAuditMonitor.NormalizeAccessType(mask);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeAccessType_NullOrEmpty_ReturnsUnknown()
    {
        Assert.Equal("unknown", FileAuditMonitor.NormalizeAccessType(null));
        Assert.Equal("unknown", FileAuditMonitor.NormalizeAccessType(""));
        Assert.Equal("unknown", FileAuditMonitor.NormalizeAccessType("  "));
    }

    [Fact]
    public void NormalizeAccessType_CompositeMaskWithWrite_ReturnsWriteData()
    {
        // 0x3 = read (0x1) + write (0x2) — write takes priority
        var result = FileAuditMonitor.NormalizeAccessType("0x3");
        Assert.Equal("write_data", result);
    }

    [Fact]
    public void NormalizeAccessType_CompositeMaskWithReadOnly_ReturnsReadData()
    {
        // 0x81 = read_data (0x1) + read_attributes (0x80)
        var result = FileAuditMonitor.NormalizeAccessType("0x81");
        Assert.Equal("read_data", result);
    }

    [Fact]
    public void NormalizeAccessType_CompositeMaskWithDelete_ReturnsDelete()
    {
        // 0x10080 = delete (0x10000) + read_attributes (0x80)
        var result = FileAuditMonitor.NormalizeAccessType("0x10080");
        Assert.Equal("delete", result);
    }

    [Fact]
    public void NormalizeAccessType_CompositeMaskWithExecute_ReturnsExecute()
    {
        // 0xA0 = read_attributes (0x80) + execute (0x20)
        var result = FileAuditMonitor.NormalizeAccessType("0xA0");
        Assert.Equal("execute", result);
    }

    [Fact]
    public void NormalizeAccessType_UnrecognizedHex_ReturnsOther()
    {
        // 0x800 is not mapped
        var result = FileAuditMonitor.NormalizeAccessType("0x800");
        Assert.Equal("other", result);
    }

    [Fact]
    public void NormalizePath_ValidPath_ReturnsNormalized()
    {
        var result = FileAuditMonitor.NormalizePath(@"D:\Projects\");
        Assert.Equal(@"D:\Projects", result);
    }

    [Fact]
    public void NormalizePath_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(FileAuditMonitor.NormalizePath(null));
        Assert.Null(FileAuditMonitor.NormalizePath(""));
        Assert.Null(FileAuditMonitor.NormalizePath("  "));
    }

    [Fact]
    public void NormalizePath_TrailingBackslash_Trimmed()
    {
        var result = FileAuditMonitor.NormalizePath(@"C:\Users\test\");
        Assert.NotNull(result);
        Assert.False(result.EndsWith('\\'));
    }

    [Fact]
    public void IsInMonitoredDirectory_FileInDirectory_ReturnsTrue()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.True(monitor.IsInMonitoredDirectory(@"D:\Projects\MyApp\file.txt"));
    }

    [Fact]
    public void IsInMonitoredDirectory_FileNotInDirectory_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.False(monitor.IsInMonitoredDirectory(@"C:\Windows\System32\cmd.exe"));
    }

    [Fact]
    public void IsInMonitoredDirectory_ExactDirectoryMatch_ReturnsTrue()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.True(monitor.IsInMonitoredDirectory(@"D:\Projects"));
    }

    [Fact]
    public void IsInMonitoredDirectory_PartialNameMatch_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.False(monitor.IsInMonitoredDirectory(@"D:\ProjectsBackup\file.txt"));
    }

    [Fact]
    public void IsInMonitoredDirectory_CaseInsensitive_ReturnsTrue()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.True(monitor.IsInMonitoredDirectory(@"d:\projects\file.txt"));
    }

    [Fact]
    public void IsInMonitoredDirectory_MultipleDirectories_ChecksAll()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects", @"D:\Documents");
        Assert.True(monitor.IsInMonitoredDirectory(@"D:\Documents\report.pdf"));
        Assert.True(monitor.IsInMonitoredDirectory(@"D:\Projects\app\main.cs"));
        Assert.False(monitor.IsInMonitoredDirectory(@"D:\Downloads\file.zip"));
    }

    [Fact]
    public void IsInMonitoredDirectory_EmptyPath_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.False(monitor.IsInMonitoredDirectory(""));
    }

    [Fact]
    public void IsDuplicateEvent_FirstEvent_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        Assert.False(monitor.IsDuplicateEvent(@"D:\Projects\file.txt", "read_data"));
    }

    [Fact]
    public void IsDuplicateEvent_SameEventTwice_ReturnsTrueOnSecond()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        monitor.IsDuplicateEvent(@"D:\Projects\file.txt", "read_data");
        Assert.True(monitor.IsDuplicateEvent(@"D:\Projects\file.txt", "read_data"));
    }

    [Fact]
    public void IsDuplicateEvent_DifferentAccessType_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        monitor.IsDuplicateEvent(@"D:\Projects\file.txt", "read_data");
        Assert.False(monitor.IsDuplicateEvent(@"D:\Projects\file.txt", "write_data"));
    }

    [Fact]
    public void IsDuplicateEvent_DifferentFile_ReturnsFalse()
    {
        var monitor = CreateMonitorWithDirectories(@"D:\Projects");
        monitor.IsDuplicateEvent(@"D:\Projects\file1.txt", "read_data");
        Assert.False(monitor.IsDuplicateEvent(@"D:\Projects\file2.txt", "read_data"));
    }

    private static FileAuditMonitor CreateMonitorWithDirectories(params string[] directories)
    {
        var options = new AgentOptions
        {
            FileAudit = new FileAuditOptions
            {
                Enabled = true,
                Directories = [.. directories],
                DuplicateWindowSeconds = 5
            }
        };

        var monitor = new FileAuditMonitor(
            NSubstitute.Substitute.For<LaptopGuardian.Agent.Storage.IEventStore>(),
            NSubstitute.Substitute.For<LaptopGuardian.Agent.Identity.IDeviceIdentityService>(),
            new OptionsWrapper<AgentOptions>(options),
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<FileAuditMonitor>>());

        // Manually initialize the directories by calling the internal method path
        foreach (var dir in directories)
        {
            var normalized = FileAuditMonitor.NormalizePath(dir);
            if (normalized is not null)
            {
                // Access the private field via reflection to set up for testing
                var field = typeof(FileAuditMonitor).GetField(
                    "_normalizedDirectories",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var set = (HashSet<string>)field!.GetValue(monitor)!;
                set.Add(normalized);
            }
        }

        return monitor;
    }
}
