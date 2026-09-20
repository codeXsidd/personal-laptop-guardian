namespace LaptopGuardian.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string DataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LaptopGuardian");

    public string DatabaseFileName { get; set; } = "guardian.db";

    public string IdentityFileName { get; set; } = "device-identity.json";

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int SyncIntervalSeconds { get; set; } = 30;

    public int SyncBatchSize { get; set; } = 50;

    public int MaxRetryCount { get; set; } = 10;

    public string SupabaseUrl { get; set; } = string.Empty;

    public string SupabaseAnonKey { get; set; } = string.Empty;

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    public string IdentityPath => Path.Combine(DataDirectory, IdentityFileName);

    public FileAuditOptions FileAudit { get; set; } = new();

    public EventLogMonitorOptions EventLogMonitor { get; set; } = new();

    public SystemMetricsOptions SystemMetrics { get; set; } = new();
}

public sealed class FileAuditOptions
{
    public bool Enabled { get; set; }

    public List<string> Directories { get; set; } = [];

    public int DuplicateWindowSeconds { get; set; } = 5;
}

public sealed class EventLogMonitorOptions
{
    public bool Enabled { get; set; } = true;

    public List<EventLogChannelConfig> Channels { get; set; } = [];
}

public sealed class EventLogChannelConfig
{
    public string Name { get; set; } = string.Empty;

    public List<string> Levels { get; set; } = [];

    public List<int>? EventIds { get; set; }
}

public sealed class SystemMetricsOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 300;

    public int MinChangePercentForEvent { get; set; } = 5;
}
