using Giim.Connectors.People;
using Giim.Infrastructure.People;
using Microsoft.Extensions.Options;

namespace Giim.Workers;

/// <summary>Runs the staff directory sync on a schedule (People:SyncInterval). A failed run is logged and retried next time.</summary>
internal sealed partial class PeopleSyncWorker(
    IServiceScopeFactory scopes, IOptions<PeopleOptions> options, ILogger<PeopleSyncWorker> logger) : BackgroundService
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
                var run = await scope.ServiceProvider.GetRequiredService<PeopleSyncService>().SyncAsync(stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff directory sync disabled (People:SyncInterval is zero).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff directory sync complete: {Seen} people, {Added} new, {Removed} no longer in the directory.")]
    private static partial void LogCompleted(ILogger logger, int seen, int added, int removed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Staff directory sync failed; will retry at the next interval.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
