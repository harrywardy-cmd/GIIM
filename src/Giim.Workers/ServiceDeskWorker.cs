using Giim.Infrastructure.ServiceDesk;

namespace Giim.Workers;

/// <summary>
/// Every 30 seconds: turns tickets ServiceDesk Plus told GIIM about into checklists, queues notes for changes, and
/// sends queued notes. A failed round is logged and retried; nothing is lost in between.
/// </summary>
internal sealed partial class ServiceDeskWorker(IServiceScopeFactory scopes, ILogger<ServiceDeskWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ServiceDeskSync>();
                var tickets = await sync.ProcessInboxAsync(batchSize: 10, stoppingToken);
                var queued = await sync.QueueStatusNotesAsync(stoppingToken);
                var sent = await sync.SendUpdatesAsync(batchSize: 20, stoppingToken);
                if (tickets + queued + sent > 0) LogRound(logger, tickets, queued, sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed round must not stop the worker; work stays queued for the next one.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ServiceDesk Plus: {Tickets} tickets processed, {Queued} notes queued, {Sent} notes sent.")]
    private static partial void LogRound(ILogger logger, int tickets, int queued, int sent);

    [LoggerMessage(Level = LogLevel.Error, Message = "ServiceDesk Plus sync failed; will retry.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
