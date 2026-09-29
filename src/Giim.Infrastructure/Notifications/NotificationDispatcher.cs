using Giim.Connectors.Email;
using Giim.Domain.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Giim.Infrastructure.Notifications;

/// <summary>
/// Sends emails waiting in the outbox. Each is saved as sent (or failed, with the error and the next retry time)
/// straight after its attempt, so a crash part-way never sends one twice or loses one.
/// </summary>
public sealed partial class NotificationDispatcher(GiimDbContext db, IEmailSender sender, TimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    /// <summary>An email that couldn't be sent within this time is out of date and won't be sent.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(3);

    public async Task<int> SendDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var notification in due)
        {
            if (now - notification.CreatedAt > Expiry)
            {
                notification.Expire(now);
                LogExpired(logger, notification.Kind, notification.Id);
            }
            else
            {
                try
                {
                    await sender.SendAsync(new EmailMessage(notification.ToAddress, notification.ToName, notification.Subject,
                        notification.BodyText, notification.BodyHtml), cancellationToken);
                    notification.MarkSent(clock.GetUtcNow());
                    sent++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // One failing email must not stop the others; the error is kept for retry.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    notification.MarkFailed(e.Message, clock.GetUtcNow());
                    LogFailed(logger, notification.Kind, notification.Id, notification.Attempts, e);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {Kind} {Id} failed (attempt {Attempts}); will retry.")]
    private static partial void LogFailed(ILogger logger, string kind, Guid id, int attempts, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {Kind} {Id} expired before it could be sent.")]
    private static partial void LogExpired(ILogger logger, string kind, Guid id);
}
