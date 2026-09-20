using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LaptopGuardian.Agent.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SupabaseIntegrationTests
{
    private static string? SupabaseUrl => Environment.GetEnvironmentVariable("SUPABASE_URL");
    private static string? SupabaseAnonKey => Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");

    private static bool IsConfigured => !string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseAnonKey);

    private SupabaseBackendClient CreateClient()
    {
        var factory = new IntegrationHttpClientFactory(SupabaseUrl!, SupabaseAnonKey!);
        return new SupabaseBackendClient(factory, NullLogger<SupabaseBackendClient>.Instance);
    }

    [Fact]
    public async Task RegisterDevice_ReturnsDeviceIdAndApiKey()
    {
        if (!IsConfigured) return;

        var client = CreateClient();
        var result = await client.RegisterDeviceAsync(
            $"IntegrationTest-{Environment.MachineName}",
            Environment.OSVersion.VersionString,
            "test-1.0.0");

        Assert.False(string.IsNullOrEmpty(result.DeviceId));
        Assert.StartsWith("lg_dk_", result.ApiKey);
        Assert.Equal(6, result.PairingCode.Length);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Heartbeat_Returns401_WhenDeviceNotPaired()
    {
        if (!IsConfigured) return;

        var client = CreateClient();
        var registration = await client.RegisterDeviceAsync(
            $"HeartbeatTest-{Environment.MachineName}",
            Environment.OSVersion.VersionString,
            "test-1.0.0");

        await Assert.ThrowsAsync<BackendAuthenticationException>(() =>
            client.SendHeartbeatAsync(registration.ApiKey));
    }

    [Fact]
    public async Task IngestEvents_Returns401_WhenDeviceNotPaired()
    {
        if (!IsConfigured) return;

        var client = CreateClient();
        var registration = await client.RegisterDeviceAsync(
            $"IngestTest-{Environment.MachineName}",
            Environment.OSVersion.VersionString,
            "test-1.0.0");

        var events = new List<DeviceEvent>
        {
            DeviceEvent.Create("test-device", EventType.AgentStarted, EventSeverity.Info,
                new Dictionary<string, object> { ["test"] = true })
        };

        await Assert.ThrowsAsync<BackendAuthenticationException>(() =>
            client.IngestEventsAsync(registration.ApiKey, events));
    }

    [Fact]
    public async Task RegisterDevice_DuplicateRegistration_CreatesNewDevice()
    {
        if (!IsConfigured) return;

        var client = CreateClient();

        var first = await client.RegisterDeviceAsync("DupTest", null, null);
        var second = await client.RegisterDeviceAsync("DupTest", null, null);

        Assert.NotEqual(first.DeviceId, second.DeviceId);
        Assert.NotEqual(first.ApiKey, second.ApiKey);
    }

    private sealed class IntegrationHttpClientFactory : IHttpClientFactory
    {
        private readonly string _baseUrl;
        private readonly string _anonKey;

        public IntegrationHttpClientFactory(string baseUrl, string anonKey)
        {
            _baseUrl = baseUrl.TrimEnd('/') + "/";
            _anonKey = anonKey;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("apikey", _anonKey);
            return client;
        }
    }
}
