using LaptopGuardian.Agent;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.Storage;
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

    builder.Services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
    builder.Services.AddSingleton<IEventStore, SqliteEventStore>();
    builder.Services.AddSingleton<IEventMonitor, StartupMonitor>();
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
