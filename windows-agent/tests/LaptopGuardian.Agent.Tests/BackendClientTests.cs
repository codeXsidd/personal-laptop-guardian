using System.Net;
using System.Text;
using System.Text.Json;
using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LaptopGuardian.Agent.Tests;

public sealed class BackendClientTests
{
    private static SupabaseBackendClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        var factory = new TestHttpClientFactory(handler, "https://test.supabase.co/");
        return new SupabaseBackendClient(factory, NullLogger<SupabaseBackendClient>.Instance);
    }

    [Fact]
    public async Task RegisterDeviceAsync_ReturnsCorrectResponse()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            device_id = "00000000-0000-0000-0000-000000000001",
            api_key = "lg_dk_testapikey",
            pairing_code = "ABC123",
            expires_at = "2026-09-20T15:30:00.000Z"
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var client = CreateClientWithHandler(handler);

        var result = await client.RegisterDeviceAsync("TEST-PC", "Windows 11", "1.0.0");

        Assert.Equal("00000000-0000-0000-0000-000000000001", result.DeviceId);
        Assert.Equal("lg_dk_testapikey", result.ApiKey);
        Assert.Equal("ABC123", result.PairingCode);
        Assert.Contains("register-device", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task IngestEventsAsync_SendsApiKeyHeader()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            inserted = 2,
            duplicates = 0,
            device_id = "device-1"
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var client = CreateClientWithHandler(handler);

        var events = new List<DeviceEvent>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                EventType = EventType.AgentStarted,
                Severity = EventSeverity.Info,
                Timestamp = DateTimeOffset.UtcNow,
                PayloadJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        await client.IngestEventsAsync("lg_dk_mykey", events);

        Assert.Equal("lg_dk_mykey", handler.LastRequest?.Headers.GetValues("x-device-api-key").First());
        Assert.Contains("ingest-events", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task SendHeartbeatAsync_ReturnsServerTime()
    {
        var serverTime = "2026-09-20T15:30:00.000Z";
        var responseJson = JsonSerializer.Serialize(new
        {
            status = "ok",
            device_id = "device-1",
            server_time = serverTime
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var client = CreateClientWithHandler(handler);

        var result = await client.SendHeartbeatAsync("lg_dk_key");

        Assert.Equal("ok", result.Status);
        Assert.Equal("device-1", result.DeviceId);
        Assert.Contains("heartbeat", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ThrowsBackendAuthenticationException_On401()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized, """{"error":"Invalid key"}""");
        var client = CreateClientWithHandler(handler);

        await Assert.ThrowsAsync<BackendAuthenticationException>(() =>
            client.SendHeartbeatAsync("bad_key"));
    }

    [Fact]
    public async Task ThrowsBackendRateLimitException_On429()
    {
        var handler = new FakeHttpHandler((HttpStatusCode)429, """{"error":"Rate limited"}""");
        var client = CreateClientWithHandler(handler);

        await Assert.ThrowsAsync<BackendRateLimitException>(() =>
            client.SendHeartbeatAsync("key"));
    }

    [Fact]
    public async Task ThrowsBackendException_On500()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, """{"error":"Server error"}""");
        var client = CreateClientWithHandler(handler);

        var ex = await Assert.ThrowsAsync<BackendException>(() =>
            client.SendHeartbeatAsync("key"));
        Assert.Equal(500, ex.StatusCode);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        public FakeHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _baseUrl;

        public TestHttpClientFactory(HttpMessageHandler handler, string baseUrl)
        {
            _handler = handler;
            _baseUrl = baseUrl;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri(_baseUrl)
            };
            return client;
        }
    }
}
