using Giim.Domain.Common;

namespace Giim.Domain.Integration;

public enum InboundStatus
{
    Pending,
    /// <summary>A checklist was created.</summary>
    Processed,
    /// <summary>Not a starter or leaver ticket, or it already has a checklist.</summary>
    Ignored,
    /// <summary>GIIM couldn't create the checklist (e.g. an unknown department); a person needs to look.</summary>
    NeedsAttention,
    /// <summary>ServiceDesk Plus couldn't be reached after several tries.</summary>
    Failed,
}

public enum TicketKind { Unknown, Starter, Leaver }

/// <summary>
/// A ticket ServiceDesk Plus told GIIM about (its webhook). Only the ticket's ID is taken from the webhook; the
/// workers fetch the ticket itself from ServiceDesk Plus, so a forged webhook can't create anything.
/// </summary>
public sealed class ServiceDeskInboundEvent : Entity
{
    public const int MaxAttempts = 6;

    public required string RequestKey { get; init; }
    public string? DisplayId { get; private set; }
    public TicketKind Kind { get; private set; }
    public InboundStatus Status { get; private set; } = InboundStatus.Pending;
    public string? Message { get; private set; }
    public Guid? CaseId { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }

    public static ServiceDeskInboundEvent Receive(string requestKey, DateTimeOffset now) =>
        new() { RequestKey = requestKey, CreatedAt = now, NextAttemptAt = now };

    public void Identify(string displayId, TicketKind kind)
    {
        DisplayId = displayId;
        Kind = kind;
    }

    public void Processed(Guid caseId, string message, DateTimeOffset now) => Finish(InboundStatus.Processed, message, now, caseId);
    public void Ignore(string message, DateTimeOffset now) => Finish(InboundStatus.Ignored, message, now);
    public void NeedsAttention(string message, DateTimeOffset now) => Finish(InboundStatus.NeedsAttention, message, now);

    /// <summary>ServiceDesk Plus couldn't be reached: try again after 1, 2, 4, 8, 16 minutes, then give up.</summary>
    public void Retry(string error, DateTimeOffset now)
    {
        Attempts++;
        Message = error.Length > 1000 ? error[..1000] : error;
        UpdatedAt = now;
        if (Attempts >= MaxAttempts) { Status = InboundStatus.Failed; return; }
        NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, Attempts - 1));
    }

    /// <summary>An administrator asks for it to be processed again (e.g. after adding the missing department).</summary>
    public void Requeue(DateTimeOffset now)
    {
        if (Status is not (InboundStatus.NeedsAttention or InboundStatus.Failed))
            throw new DomainException("Only tickets that need attention or failed can be retried.");
        Status = InboundStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        UpdatedAt = now;
    }

    private void Finish(InboundStatus status, string message, DateTimeOffset now, Guid? caseId = null)
    {
        Attempts++;
        Status = status;
        Message = message.Length > 1000 ? message[..1000] : message;
        CaseId = caseId ?? CaseId;
        UpdatedAt = now;
    }
}

public enum UpdateKind { Note, Resolve }

public enum UpdateStatus { Pending, Sent, Failed }

/// <summary>
/// A note (or resolution) waiting to be added to a ServiceDesk Plus ticket, saved with the change it reports so it is
/// never lost; the workers send it with retries.
/// </summary>
public sealed class ServiceDeskUpdate : Entity
{
    public const int MaxAttempts = 8;

    /// <summary>The ticket number people see; <see cref="RequestKey"/> is looked up from it if not known.</summary>
    public required string DisplayId { get; init; }
    public string? RequestKey { get; set; }
    public UpdateKind Kind { get; init; }
    public required string Content { get; init; }
    public Guid? CaseId { get; init; }
    public Guid? DeviceRequestId { get; init; }

    public UpdateStatus Status { get; private set; } = UpdateStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    public static ServiceDeskUpdate Create(string displayId, string? requestKey, UpdateKind kind, string content, DateTimeOffset now,
        Guid? caseId = null, Guid? deviceRequestId = null) => new()
    {
        DisplayId = displayId,
        RequestKey = requestKey,
        Kind = kind,
        Content = content,
        CaseId = caseId,
        DeviceRequestId = deviceRequestId,
        CreatedAt = now,
        NextAttemptAt = now,
    };

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = UpdateStatus.Sent;
        SentAt = now;
        LastError = null;
        UpdatedAt = now;
    }

    /// <summary>Retries after 1, 2, 4… minutes (at most 2 hours apart); <paramref name="permanent"/> gives up at once.</summary>
    public void MarkFailed(string error, DateTimeOffset now, bool permanent = false)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        UpdatedAt = now;
        if (permanent || Attempts >= MaxAttempts) { Status = UpdateStatus.Failed; return; }
        NextAttemptAt = now + TimeSpan.FromMinutes(Math.Min(120, Math.Pow(2, Attempts - 1)));
    }
}
