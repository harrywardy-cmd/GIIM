using Giim.Infrastructure.Notifications;

namespace Giim.Workers;

/// <summary>
/// Every 5 minutes: queues manager emails and reminders that have become due, and the daily and weekly digests at
/// their set time. Each record remembers what was sent, so checking often never sends anything twice.
/// </summary>
internal sealed partial class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var done = await scope.ServiceProvider.GetRequiredService<ReminderService>().RunDueAsync(stoppingToken);
                if (done.Count > 0) LogDone(logger, done);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed round must not stop the worker; the next round catches up.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reminders queued: {Done}")]
    private static partial void LogDone(ILogger logger, IReadOnlyList<string> done);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reminders failed; will retry.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
