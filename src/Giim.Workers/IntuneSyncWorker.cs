using Giim.Connectors.Intune;
using Giim.Infrastructure.Devices;
using Microsoft.Extensions.Options;

namespace Giim.Workers;

/// <summary>Runs the Intune sync on a schedule (Intune:SyncInterval). A failed run is logged and retried next time.</summary>
internal sealed partial class IntuneSyncWorker(
    IServiceScopeFactory scopes, IOptions<IntuneOptions> options, ILogger<IntuneSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SyncInterval;
        if (interval <= TimeSpan.Zero)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var run = await scope.ServiceProvider.GetRequiredService<IntuneSyncService>().SyncAsync(stoppingToken);
                LogCompleted(logger, run.DevicesSeen, run.Added, run.Removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed sync must not stop the worker; it is recorded in SyncRuns and retried.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Intune sync disabled (Intune:SyncInterval is zero).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Intune sync complete: {Seen} devices, {Added} new, {Removed} removed.")]
    private static partial void LogCompleted(ILogger logger, int seen, int added, int removed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Intune sync failed; will retry at the next interval.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
