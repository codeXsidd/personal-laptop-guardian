using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LaptopGuardian.Desktop.Services;

public sealed class EventStoreReader
{
    private static readonly string DefaultDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LaptopGuardian", "guardian.db");

    private readonly string _connectionString;

    public EventStoreReader(string? dbPath = null)
    {
        var path = dbPath ?? DefaultDbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<List<EventRecord>> GetRecentEventsAsync(int limit = 50)
    {
        var events = new List<EventRecord>();
        if (!File.Exists(DefaultDbPath)) return events;

        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, event_type, severity, timestamp, payload_json, sync_status, created_at
                FROM events
                ORDER BY timestamp DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new EventRecord
                {
                    Id = reader.GetString(0),
                    EventType = reader.GetString(1),
                    Severity = reader.GetString(2),
                    Timestamp = DateTimeOffset.Parse(reader.GetString(3)),
                    PayloadJson = reader.GetString(4),
                    SyncStatus = reader.GetString(5),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                });
            }
        }
        catch
        {
            // Database may be locked or not yet created
        }

        return events;
    }

    public async Task<int> GetPendingCountAsync()
    {
        if (!File.Exists(DefaultDbPath)) return 0;
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM events WHERE sync_status = 'pending'";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch { return 0; }
    }
}

public sealed class EventRecord
{
    public required string Id { get; init; }
    public required string EventType { get; init; }
    public required string Severity { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string PayloadJson { get; init; }
    public required string SyncStatus { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public string DisplayName => EventType switch
    {
        "agent_started" => "Agent Started",
        "system_startup" => "System Startup",
        "system_shutdown" => "System Shutdown",
        "session_login" => "Login",
        "session_logout" => "Logout",
        "session_lock" => "Screen Lock",
        "session_unlock" => "Screen Unlock",
        "login_failed" => "Login Failed",
        "process_start" => "App Started",
        "process_stop" => "App Stopped",
        "usb_connected" => "USB Connected",
        "usb_disconnected" => "USB Disconnected",
        "network_connected" => "Network Connected",
        "network_disconnected" => "Network Disconnected",
        "network_changed" => "Network Changed",
        "eventlog_entry" => "Event Log",
        "file_access" => "File Access",
        "system_metrics" => "System Metrics",
        _ => EventType.Replace('_', ' '),
    };

    public string SeverityIcon => Severity switch
    {
        "critical" => "🔴",
        "high" => "🟠",
        "medium" => "🟡",
        "low" => "🔵",
        _ => "⚪",
    };

    public string TimeAgo
    {
        get
        {
            var diff = DateTimeOffset.UtcNow - Timestamp;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }

    public string Subtitle
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(PayloadJson);
                var root = doc.RootElement;

                return EventType switch
                {
                    "session_login" or "session_logout" or "session_lock" or "session_unlock"
                        => GetString(root, "username") ?? "",
                    "login_failed"
                        => $"{GetString(root, "username") ?? "Unknown"} — {GetString(root, "failure_reason") ?? ""}",
                    "process_start" => FormatProcessStart(root),
                    "process_stop" => FormatProcessStop(root),
                    "usb_connected" or "usb_disconnected"
                        => GetString(root, "device_name") ?? "",
                    "network_connected" or "network_changed" => FormatNetwork(root),
                    "network_disconnected"
                        => GetString(root, "adapter_name") ?? "",
                    "eventlog_entry"
                        => $"[{GetString(root, "level") ?? ""}] {GetString(root, "source") ?? ""}: {GetString(root, "message") ?? ""}",
                    "file_access"
                        => $"{GetString(root, "access_type") ?? ""} {GetString(root, "file_path") ?? ""}",
                    "system_metrics"
                        => $"CPU {GetString(root, "cpu") ?? "N/A"}% | Mem {GetString(root, "memory") ?? "N/A"}%",
                    "agent_started"
                        => $"Version {GetString(root, "version") ?? ""}",
                    _ => "",
                };
            }
            catch
            {
                return "";
            }
        }
    }

    private static string FormatProcessStart(JsonElement root)
    {
        var name = GetString(root, "process_name") ?? "";
        var title = GetString(root, "window_title");
        if (!string.IsNullOrEmpty(title)) return $"{name} — {title}";
        return $"{name} (PID {GetString(root, "pid") ?? ""})";
    }

    private static string FormatProcessStop(JsonElement root)
    {
        var name = GetString(root, "process_name") ?? "";
        var durStr = GetString(root, "duration_seconds");
        if (durStr is not null && double.TryParse(durStr, out var dur))
        {
            var secs = (int)dur;
            if (secs >= 3600) return $"{name} ran {secs / 3600}h {secs % 3600 / 60}m";
            if (secs >= 60) return $"{name} ran {secs / 60}m {secs % 60}s";
            return $"{name} ran {secs}s";
        }
        return $"{name} (PID {GetString(root, "pid") ?? ""})";
    }

    private static string FormatNetwork(JsonElement root)
    {
        if (root.TryGetProperty("adapters", out var adapters) && adapters.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in adapters.EnumerateArray())
            {
                var parts = new List<string>();
                var ssid = GetString(a, "ssid");
                var ipv4 = GetString(a, "ipv4");
                var signal = GetString(a, "signal");
                if (ssid is not null) parts.Add(ssid);
                if (ipv4 is not null) parts.Add(ipv4);
                if (signal is not null) parts.Add(signal);
                if (parts.Count > 0) return string.Join(" · ", parts);
            }
        }
        var adapterName = GetString(root, "adapter_name");
        var ip = GetString(root, "ip_address") ?? GetString(root, "ipv4");
        return $"{adapterName ?? ""} {ip ?? ""}".Trim();
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var val) && val.ValueKind != JsonValueKind.Null)
            return val.ToString();
        return null;
    }
}
