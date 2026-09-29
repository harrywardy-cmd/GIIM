using Giim.Domain.Common;

namespace Giim.Domain.Notifications;

public enum NotificationStatus { Pending, Sent, Failed }

/// <summary>
/// An email waiting to be sent, saved in the same transaction as the change it announces (an "outbox"), so a mail
/// outage delays emails but never loses them. The background workers send pending ones and retry failures with
/// increasing gaps, giving up after <see cref="MaxAttempts"/>.
/// </summary>
public sealed class Notification : Entity
{
    public const int MaxAttempts = 8;

    public required string Kind { get; init; }
    public required string ToAddress { get; init; }
    public string? ToName { get; init; }
    public required string Subject { get; init; }
    public required string BodyText { get; init; }
    public required string BodyHtml { get; init; }
    /// <summary>The request it is about, if any.</summary>
    public Guid? RequestId { get; init; }

    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    public static Notification Create(string kind, string toAddress, string? toName, string subject, string bodyText, string bodyHtml,
        Guid? requestId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toAddress) || !toAddress.Contains('@', StringComparison.Ordinal))
            throw new DomainException($"'{toAddress}' is not an email address.");
        return new Notification
        {
            Kind = kind,
            ToAddress = toAddress.Trim(),
            ToName = toName,
            Subject = subject,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            RequestId = requestId,
            CreatedAt = now,
            NextAttemptAt = now,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt = now;
        LastError = null;
        UpdatedAt = now;
    }

    /// <summary>Too old to be useful (e.g. email was switched off for days); it won't be sent.</summary>
    public void Expire(DateTimeOffset now)
    {
        Status = NotificationStatus.Failed;
        LastError = "Expired before it could be sent.";
        UpdatedAt = now;
    }

    /// <summary>Retries after 1, 2, 4, 8... minutes (at most 2 hours apart), then gives up.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        UpdatedAt = now;
        if (Attempts >= MaxAttempts)
        {
            Status = NotificationStatus.Failed;
            return;
        }
        NextAttemptAt = now + TimeSpan.FromMinutes(Math.Min(120, Math.Pow(2, Attempts - 1)));
    }
}
