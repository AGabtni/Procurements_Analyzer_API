using ProcurePortal.API.Services;

namespace ProcurePortal.API.Workers;

/// <summary>
/// Background worker that triggers expiry deadline notifications on a
/// configurable interval (default 5 minutes). Stamp-based deduplication
/// in ExpiryNotificationService ensures no duplicate emails regardless of
/// how frequently this runs.
///
/// Configure via appsettings: "App:ExpiryCheckIntervalSeconds" (default 300).
/// </summary>
public class ExpiryNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ExpiryNotificationWorker> _logger;

    public ExpiryNotificationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ExpiryNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    private TimeSpan Interval
    {
        get
        {
            var seconds = _config.GetValue<int>("App:ExpiryCheckIntervalSeconds", 300);
            return TimeSpan.FromSeconds(Math.Max(30, seconds)); // floor at 30s
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpiryNotificationWorker started — interval {Seconds}s", Interval.TotalSeconds);

        // Run once immediately on startup so activating notifications takes effect
        // without waiting for the first interval to elapse.
        await RunCheckAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunCheckAsync(stoppingToken);
        }

        _logger.LogInformation("ExpiryNotificationWorker stopped");
    }

    private async Task RunCheckAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ExpiryNotificationService>();
            var sent = await service.RunAsync();
            if (sent > 0)
                _logger.LogInformation("ExpiryNotificationWorker: {Sent} digest(s) sent", sent);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "ExpiryNotificationWorker: unhandled error during check");
        }
    }
}
