using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Storage;

public sealed class SqliteEventStore : IEventStore, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteEventStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteEventStore(IOptions<AgentOptions> options, ILogger<SqliteEventStore> logger)
    {
        var dbPath = options.Value.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _logger = logger;
        _logger.LogInformation("SQLite database path: {DatabasePath}", dbPath);
    }

    internal SqliteEventStore(string connectionString, ILogger<SqliteEventStore> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SQLite event store");

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Enable WAL mode for concurrent read/write
            await using (var walCmd = connection.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                await walCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Set busy timeout to prevent "database is locked" errors
            await using (var busyCmd = connection.CreateCommand())
            {
                busyCmd.CommandText = "PRAGMA busy_timeout=5000;";
                await busyCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS events (
                    id TEXT PRIMARY KEY,
                    event_type TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    payload_json TEXT NOT NULL DEFAULT '{}',
                    sync_status TEXT NOT NULL DEFAULT 'pending',
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    synced_at TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_events_sync_status ON events (sync_status);
                CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events (timestamp DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("SQLite event store initialized");
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "SQLite database corrupted, attempting recovery");

            var builder = new SqliteConnectionStringBuilder(_connectionString);
            var dbPath = builder.DataSource;
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var corruptPath = $"{dbPath}.corrupt.{timestamp}";

            File.Move(dbPath, corruptPath);
            _logger.LogWarning("Renamed corrupted database to {CorruptPath}", corruptPath);

            // Retry with fresh database
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var walCmd = connection.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                await walCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var busyCmd = connection.CreateCommand())
            {
                busyCmd.CommandText = "PRAGMA busy_timeout=5000;";
                await busyCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS events (
                    id TEXT PRIMARY KEY,
                    event_type TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    payload_json TEXT NOT NULL DEFAULT '{}',
                    sync_status TEXT NOT NULL DEFAULT 'pending',
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    synced_at TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_events_sync_status ON events (sync_status);
                CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events (timestamp DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogWarning("SQLite event store initialized with fresh database after recovery");
        }
    }

    public async Task InsertEventAsync(DeviceEvent deviceEvent, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO events (id, event_type, severity, timestamp, payload_json, sync_status, retry_count, created_at, synced_at)
                VALUES (@id, @eventType, @severity, @timestamp, @payloadJson, @syncStatus, @retryCount, @createdAt, @syncedAt)
                """;

            command.Parameters.AddWithValue("@id", deviceEvent.Id);
            command.Parameters.AddWithValue("@eventType", deviceEvent.EventType);
            command.Parameters.AddWithValue("@severity", deviceEvent.Severity);
            command.Parameters.AddWithValue("@timestamp", deviceEvent.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("@payloadJson", deviceEvent.PayloadJson);
            command.Parameters.AddWithValue("@syncStatus", deviceEvent.SyncStatus);
            command.Parameters.AddWithValue("@retryCount", deviceEvent.RetryCount);
            command.Parameters.AddWithValue("@createdAt", deviceEvent.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("@syncedAt", (object?)deviceEvent.SyncedAt?.ToString("O") ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Inserted event {EventId} ({EventType})", deviceEvent.Id, deviceEvent.EventType);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceEvent>> GetPendingEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, event_type, severity, timestamp, payload_json, sync_status, retry_count, created_at, synced_at
            FROM events
            WHERE sync_status = 'pending'
            ORDER BY timestamp ASC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var events = new List<DeviceEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    public async Task MarkSyncedAsync(IEnumerable<string> eventIds, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var eventId in eventIds)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    UPDATE events SET sync_status = 'synced', synced_at = @syncedAt WHERE id = @id
                    """;
                command.Parameters.AddWithValue("@id", eventId);
                command.Parameters.AddWithValue("@syncedAt", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task MarkFailedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE events SET sync_status = 'failed', retry_count = retry_count + 1 WHERE id = @id
                """;
            command.Parameters.AddWithValue("@id", eventId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE sync_status = 'pending'";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task ResetFailedEventsAsync(int maxRetryCount, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE events SET sync_status = 'pending'
                WHERE sync_status = 'failed' AND retry_count < @maxRetryCount
                """;
            command.Parameters.AddWithValue("@maxRetryCount", maxRetryCount);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Reset {Count} failed events to pending", affected);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<int> CleanupOldEventsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM events
                WHERE sync_status = 'synced' AND synced_at < datetime('now', '-' || @retentionDays || ' days')
                """;
            command.Parameters.AddWithValue("@retentionDays", retentionDays);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} old synced events (retention: {RetentionDays} days)",
                deleted, retentionDays);

            return deleted;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static DeviceEvent ReadEvent(SqliteDataReader reader)
    {
        var syncedAtStr = reader.IsDBNull(8) ? null : reader.GetString(8);
        return new DeviceEvent
        {
            Id = reader.GetString(0),
            EventType = reader.GetString(1),
            Severity = reader.GetString(2),
            Timestamp = DateTimeOffset.Parse(reader.GetString(3)),
            PayloadJson = reader.GetString(4),
            SyncStatus = reader.GetString(5),
            RetryCount = reader.GetInt32(6),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            SyncedAt = syncedAtStr is not null ? DateTimeOffset.Parse(syncedAtStr) : null
        };
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
