using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Connectivity;

public sealed class ConnectivityTracker : IConnectivityTracker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectivityTracker> _logger;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private volatile bool _isOnline;

    internal const string HttpClientName = "Connectivity";
    private const int CheckIntervalSeconds = 30;

    public bool IsOnline => _isOnline;
    public event EventHandler<bool>? ConnectivityChanged;

    public ConnectivityTracker(IHttpClientFactory httpClientFactory, ILogger<ConnectivityTracker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _monitorTask = MonitorLoopAsync(_cts.Token);
        _logger.LogInformation("Connectivity tracker started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _cts?.Cancel();
        if (_monitorTask is not null)
        {
            try { await _monitorTask; }
            catch (OperationCanceledException) { }
        }
        _logger.LogInformation("Connectivity tracker stopped");
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        await UpdateConnectivityAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), ct);
            }
            catch (OperationCanceledException) { break; }

            await UpdateConnectivityAsync(ct);
        }
    }

    private async Task UpdateConnectivityAsync(CancellationToken ct)
    {
        var wasOnline = _isOnline;
        _isOnline = await CheckConnectivityAsync(ct);

        if (_isOnline != wasOnline)
        {
            _logger.LogInformation("Connectivity changed: {Status}", _isOnline ? "online" : "offline");
            ConnectivityChanged?.Invoke(this, _isOnline);
        }
    }

    internal async Task<bool> CheckConnectivityAsync(CancellationToken ct)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
            return false;

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(string.Empty, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Connectivity check failed");
            return false;
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _logger.LogInformation("Network availability changed: {IsAvailable}", e.IsAvailable);
        if (!e.IsAvailable && _isOnline)
        {
            _isOnline = false;
            ConnectivityChanged?.Invoke(this, false);
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
