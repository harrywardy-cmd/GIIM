using Giim.Connectors.Email;
using Giim.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace Giim.Workers;

/// <summary>Sends queued emails every 30 seconds (Email:Mode chooses how). A failed round is logged and retried.</summary>
internal sealed partial class NotificationWorker(
    IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode == EmailMode.None)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                int sent;
                do
                {
                    sent = await dispatcher.SendDueAsync(batchSize: 20, stoppingToken);
                    if (sent > 0) LogSent(logger, sent);
                }
                while (sent == 20);   // keep going while there's a backlog
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed round must not stop the worker; unsent emails stay queued.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sending is off (Email:Mode is None); emails stay queued.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {Count} emails.")]
    private static partial void LogSent(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending emails failed; will retry.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
