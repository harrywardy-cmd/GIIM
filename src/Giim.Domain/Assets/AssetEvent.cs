namespace Giim.Domain.Assets;

public enum AssetEventType
{
    Created,          // added by hand, or existed before the timeline was introduced
    Imported,         // from a legacy register
    StatusChanged,
    MarkedReady,
    Wiped,
    ReportedLost,
    ReportedStolen,
    Recovered,
    NoteAdded,
    OwnerLinked,      // legacy "Assigned To" name matched to a real person
    Assigned,
    ReturnRequested,
    Returned,
    RepairStarted,
    RepairCompleted,
    Retired,
    Disposed,
}

/// <summary>Who did it, which ticket, and why. Supplied with every lifecycle action.</summary>
public sealed record ActionContext(string Actor, string? TicketNumber = null, string? Note = null, DateTimeOffset? OccurredAt = null);

/// <summary>
/// One entry in an asset's timeline. Append-only: events are never edited or deleted (enforced when saving),
/// so the history stays trustworthy for audits.
/// </summary>
public sealed class AssetEvent
{
    public long Id { get; init; }
    public Guid AssetId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
    public AssetEventType Type { get; init; }

    public AssetStatus? FromStatus { get; init; }
    public AssetStatus? ToStatus { get; init; }

    /// <summary>Technician or user who performed the action.</summary>
    public required string Actor { get; init; }
    public string? TicketNumber { get; init; }
    public required string Summary { get; init; }
    public string? Note { get; init; }

    /// <summary>Structured extras for the event type, e.g. the circumstances of a theft.</summary>
    public string? DetailsJson { get; init; }
}
