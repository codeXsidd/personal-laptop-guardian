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
}
