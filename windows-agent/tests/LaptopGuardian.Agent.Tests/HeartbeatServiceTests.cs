using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Heartbeat;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LaptopGuardian.Agent.Tests;

public sealed class HeartbeatServiceTests : IDisposable
{
    private readonly IBackendClient _backendClient;
    private readonly IDeviceIdentityService _identityService;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly HeartbeatService _heartbeatService;
    private readonly DeviceIdentity _identity;
    private readonly string _tempDir;

    public HeartbeatServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HeartbeatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _backendClient = Substitute.For<IBackendClient>();
        _connectivityTracker = Substitute.For<IConnectivityTracker>();
        _connectivityTracker.IsOnline.Returns(true);
        _identityService = Substitute.For<IDeviceIdentityService>();

        _identity = new DeviceIdentity
        {
            DeviceId = Guid.NewGuid().ToString(),
            ApiKey = "lg_dk_test_heartbeat_key",
            ServerDeviceId = Guid.NewGuid().ToString(),
            PairingCode = "ABC123"
        };
        _identityService.GetOrCreateIdentityAsync(Arg.Any<CancellationToken>()).Returns(_identity);

        var options = Options.Create(new AgentOptions
        {
            DataDirectory = _tempDir,
            HeartbeatIntervalSeconds = 60
        });

        _heartbeatService = new HeartbeatService(
            _backendClient, _identityService, _connectivityTracker,
            options, NullLogger<HeartbeatService>.Instance);
    }

    public void Dispose()
    {
        _heartbeatService.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SendHeartbeatAsync_DetectsPairing()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HeartbeatResponse("ok", "server-device-id", DateTimeOffset.UtcNow));

        var paired = false;
        _heartbeatService.DevicePaired += (_, _) => paired = true;

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(_heartbeatService.IsPaired);
        Assert.True(paired);
        await _identityService.Received(1).SaveIdentityAsync(Arg.Any<DeviceIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_StaysUnpairedOn401()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new BackendAuthenticationException());

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(_heartbeatService.IsPaired);
    }

    [Fact]
    public async Task SendHeartbeatAsync_DoesNotFirePairedEventTwice()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HeartbeatResponse("ok", "server-device-id", DateTimeOffset.UtcNow));

        var pairedCount = 0;
        _heartbeatService.DevicePaired += (_, _) => pairedCount++;

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);
        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(1, pairedCount);
    }

    [Fact]
    public async Task SendHeartbeatAsync_SkipsWhenNotRegistered()
    {
        var unregisteredIdentity = new DeviceIdentity { DeviceId = "test" };
        _identityService.GetOrCreateIdentityAsync(Arg.Any<CancellationToken>()).Returns(unregisteredIdentity);

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        await _backendClient.DidNotReceive()
            .SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_SendsCorrectApiKey()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HeartbeatResponse("ok", "device-id", DateTimeOffset.UtcNow));

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        await _backendClient.Received(1)
            .SendHeartbeatAsync("lg_dk_test_heartbeat_key", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_HandlesNetworkError()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Network unreachable"));

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(_heartbeatService.IsPaired);
    }

    [Fact]
    public async Task SendHeartbeatAsync_DetectsRevocation()
    {
        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HeartbeatResponse("ok", "device-id", DateTimeOffset.UtcNow));

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);
        Assert.True(_heartbeatService.IsPaired);

        _backendClient.SendHeartbeatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new BackendAuthenticationException());

        await _heartbeatService.SendHeartbeatAsync(CancellationToken.None);
        Assert.False(_heartbeatService.IsPaired);
    }
}
