using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaptopGuardian.Agent.Models;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Backend;

public sealed class SupabaseBackendClient : IBackendClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupabaseBackendClient> _logger;

    internal const string HttpClientName = "Supabase";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SupabaseBackendClient(IHttpClientFactory httpClientFactory, ILogger<SupabaseBackendClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<RegisterDeviceResponse> RegisterDeviceAsync(
        string machineName, string? osVersion, string? agentVersion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Registering device: {MachineName}", machineName);

        var payload = new { machine_name = machineName, os_version = osVersion, agent_version = agentVersion };
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("functions/v1/register-device", payload, JsonOptions, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RegisterDeviceJsonResponse>(JsonOptions, cancellationToken)
            ?? throw new BackendException("Empty response from register-device");

        return new RegisterDeviceResponse(
            result.DeviceId,
            result.ApiKey,
            result.PairingCode,
            DateTimeOffset.Parse(result.ExpiresAt));
    }

    public async Task<IngestEventsResponse> IngestEventsAsync(
        string apiKey, IReadOnlyList<DeviceEvent> events,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Ingesting {Count} events", events.Count);

        var payload = new
        {
            events = events.Select(e => new
            {
                id = e.Id,
                event_type = e.EventType,
                severity = e.Severity,
                timestamp = e.Timestamp.ToString("O"),
                payload = JsonSerializer.Deserialize<JsonElement>(e.PayloadJson)
            }).ToArray()
        };

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "functions/v1/ingest-events");
        request.Headers.Add("x-device-api-key", apiKey);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<IngestEventsJsonResponse>(JsonOptions, cancellationToken)
            ?? throw new BackendException("Empty response from ingest-events");

        return new IngestEventsResponse(result.Inserted, result.Duplicates, result.DeviceId);
    }

    public async Task<HeartbeatResponse> SendHeartbeatAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending heartbeat");

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "functions/v1/heartbeat");
        request.Headers.Add("x-device-api-key", apiKey);
        request.Content = JsonContent.Create(new { }, options: JsonOptions);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<HeartbeatJsonResponse>(JsonOptions, cancellationToken)
            ?? throw new BackendException("Empty response from heartbeat");

        return new HeartbeatResponse(
            result.Status,
            result.DeviceId,
            DateTimeOffset.Parse(result.ServerTime));
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var statusCode = (int)response.StatusCode;

        throw statusCode switch
        {
            401 or 403 => new BackendAuthenticationException($"Authentication failed: {body}"),
            429 => new BackendRateLimitException(response.Headers.RetryAfter?.Delta),
            >= 500 => new BackendException($"Server error: {body}", statusCode),
            _ => new BackendException($"Request failed ({statusCode}): {body}", statusCode)
        };
    }

    private sealed record RegisterDeviceJsonResponse
    {
        [JsonPropertyName("device_id")] public required string DeviceId { get; init; }
        [JsonPropertyName("api_key")] public required string ApiKey { get; init; }
        [JsonPropertyName("pairing_code")] public required string PairingCode { get; init; }
        [JsonPropertyName("expires_at")] public required string ExpiresAt { get; init; }
    }

    private sealed record IngestEventsJsonResponse
    {
        [JsonPropertyName("inserted")] public required int Inserted { get; init; }
        [JsonPropertyName("duplicates")] public required int Duplicates { get; init; }
        [JsonPropertyName("device_id")] public required string DeviceId { get; init; }
    }

    private sealed record HeartbeatJsonResponse
    {
        [JsonPropertyName("status")] public required string Status { get; init; }
        [JsonPropertyName("device_id")] public required string DeviceId { get; init; }
        [JsonPropertyName("server_time")] public required string ServerTime { get; init; }
    }
}
