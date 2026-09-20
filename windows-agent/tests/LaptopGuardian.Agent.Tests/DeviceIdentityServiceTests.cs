using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Tests;

public sealed class DeviceIdentityServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DeviceIdentityServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LaptopGuardianTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private DeviceIdentityService CreateService()
    {
        var options = Options.Create(new AgentOptions { DataDirectory = _tempDir });
        return new DeviceIdentityService(options, NullLogger<DeviceIdentityService>.Instance);
    }

    [Fact]
    public async Task GetOrCreateIdentityAsync_CreatesNewIdentity_WhenNoneExists()
    {
        var service = CreateService();

        var identity = await service.GetOrCreateIdentityAsync();

        Assert.NotNull(identity);
        Assert.False(string.IsNullOrEmpty(identity.DeviceId));
        Assert.True(Guid.TryParse(identity.DeviceId, out _));
        Assert.Equal(Environment.MachineName, identity.MachineName);
    }

    [Fact]
    public async Task GetOrCreateIdentityAsync_PersistsIdentityToFile()
    {
        var service = CreateService();

        var identity = await service.GetOrCreateIdentityAsync();

        var expectedPath = Path.Combine(_tempDir, "device-identity.json");
        Assert.True(File.Exists(expectedPath));

        var fileContent = await File.ReadAllTextAsync(expectedPath);
        Assert.Contains(identity.DeviceId, fileContent);
    }

    [Fact]
    public async Task GetOrCreateIdentityAsync_ReturnsSameIdentity_OnSubsequentCalls()
    {
        var service = CreateService();

        var first = await service.GetOrCreateIdentityAsync();
        var second = await service.GetOrCreateIdentityAsync();

        Assert.Equal(first.DeviceId, second.DeviceId);
    }

    [Fact]
    public async Task GetOrCreateIdentityAsync_LoadsExistingIdentity_FromFile()
    {
        var firstService = CreateService();
        var originalIdentity = await firstService.GetOrCreateIdentityAsync();

        // Create a new service instance (simulates restart)
        var secondService = CreateService();
        var loadedIdentity = await secondService.GetOrCreateIdentityAsync();

        Assert.Equal(originalIdentity.DeviceId, loadedIdentity.DeviceId);
    }

    [Fact]
    public async Task GetOrCreateIdentityAsync_CreatesDirectory_IfNotExists()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "subdir");
        var options = Options.Create(new AgentOptions { DataDirectory = nestedDir });
        var service = new DeviceIdentityService(options, NullLogger<DeviceIdentityService>.Instance);

        var identity = await service.GetOrCreateIdentityAsync();

        Assert.NotNull(identity);
        Assert.True(Directory.Exists(nestedDir));
    }
}
