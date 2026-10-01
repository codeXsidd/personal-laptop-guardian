using LaptopGuardian.Agent;
using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Heartbeat;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Ipc;
using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.PcControl;
using LaptopGuardian.Agent.Storage;
using LaptopGuardian.Agent.Sync;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Laptop Guardian agent");

    var builder = Host.CreateApplicationBuilder(args);

    var agentOptions = new AgentOptions();
    builder.Configuration.GetSection(AgentOptions.SectionName).Bind(agentOptions);
    Directory.CreateDirectory(agentOptions.DataDirectory);
    Log.Information("Data directory: {DataDirectory}", agentOptions.DataDirectory);

    var logPath = Path.Combine(agentOptions.DataDirectory, "logs", "laptop-guardian-.log");
    builder.Services.AddSerilog((services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.File(
                logPath,
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    });

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "LaptopGuardian";
    });

    builder.Services.Configure<AgentOptions>(
        builder.Configuration.GetSection(AgentOptions.SectionName));

    // Identity and credential protection (Windows DPAPI)
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
    }
    builder.Services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();

    // Event storage
    builder.Services.AddSingleton<IEventStore, SqliteEventStore>();

    // Event monitors
    builder.Services.AddSingleton<IEventMonitor, StartupMonitor>();
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddSingleton<IEventMonitor, SessionMonitor>();
        builder.Services.AddSingleton<IEventMonitor, ProcessMonitor>();
        builder.Services.AddSingleton<IEventMonitor, UsbMonitor>();

        builder.Services.AddSingleton<SystemMetricsMonitor>();
        builder.Services.AddSingleton<IEventMonitor>(sp => sp.GetRequiredService<SystemMetricsMonitor>());
        builder.Services.AddSingleton<ISystemMetricsProvider>(sp => sp.GetRequiredService<SystemMetricsMonitor>());

        builder.Services.AddSingleton<IEventMonitor, EventLogMonitor>();
        builder.Services.AddSingleton<IEventMonitor, FileAuditMonitor>();
    }
    builder.Services.AddSingleton<IEventMonitor, NetworkMonitor>();

    // HTTP clients for backend communication
    builder.Services.AddHttpClient(SupabaseBackendClient.HttpClientName, client =>
    {
        if (!string.IsNullOrWhiteSpace(agentOptions.SupabaseUrl))
        {
            client.BaseAddress = new Uri(agentOptions.SupabaseUrl.TrimEnd('/') + "/");
        }
        if (!string.IsNullOrWhiteSpace(agentOptions.SupabaseAnonKey))
        {
            client.DefaultRequestHeaders.Add("apikey", agentOptions.SupabaseAnonKey);
        }
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddHttpClient(ConnectivityTracker.HttpClientName, client =>
    {
        if (!string.IsNullOrWhiteSpace(agentOptions.SupabaseUrl))
        {
            client.BaseAddress = new Uri(agentOptions.SupabaseUrl.TrimEnd('/') + "/");
        }
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    // Backend client, connectivity, sync, heartbeat
    builder.Services.AddSingleton<IBackendClient, SupabaseBackendClient>();
    builder.Services.AddSingleton<IConnectivityTracker, ConnectivityTracker>();
    builder.Services.AddSingleton<ISyncEngine, SyncEngine>();
    builder.Services.AddSingleton<IHeartbeatService, HeartbeatService>();

    builder.Services.AddSingleton<IPcControlService, PcControlService>();

    builder.Services.AddHostedService<IpcServer>();
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Agent terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
