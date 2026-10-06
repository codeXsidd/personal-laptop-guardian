using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent;

[SupportedOSPlatform("windows")]
public sealed class SessionChangeLifetime : WindowsServiceLifetime
{
    private readonly ILogger<SessionChangeLifetime> _logger;

    public static event Action<int>? SessionChanged;

    public SessionChangeLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptions)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptions)
    {
        CanHandleSessionChangeEvent = true;
        _logger = loggerFactory.CreateLogger<SessionChangeLifetime>();
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        _logger.LogInformation("SCM SessionChange: {Reason}", changeDescription.Reason);
        SessionChanged?.Invoke((int)changeDescription.Reason);
        base.OnSessionChange(changeDescription);
    }
}
