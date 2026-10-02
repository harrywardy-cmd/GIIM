using Giim.Infrastructure.Reports;

namespace Giim.Workers;

/// <summary>
/// Records the dashboard totals every hour (one row per day, so each day keeps its last count), for the dashboard's
/// week-on-week arrows.
/// </summary>
internal sealed partial class SnapshotWorker(IServiceScopeFactory scopes, ILogger<SnapshotWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DashboardHistory>().TakeSnapshotAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A missed snapshot must not stop the worker; the next hour takes it.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard snapshot failed; will try again next hour.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
