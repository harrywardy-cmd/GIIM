using Giim.Domain.Common;

namespace Giim.Domain.Assets;

/// <summary>A serialised item tracked individually through its lifecycle.</summary>
public sealed class Asset : Entity
{
    public string? AssetTag { get; set; }
    public required string SerialNumber { get; set; }
    public required string Manufacturer { get; set; }
    public required string Model { get; set; }
    public Guid CategoryId { get; set; }
    public AssetCategory? Category { get; set; }
    public string? Location { get; set; }

    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? WarrantyExpiry { get; set; }
    public string? Supplier { get; set; }
    public decimal? Cost { get; set; }

    public string? IntuneDeviceId { get; set; }
    public DateTimeOffset? LastSeenInIntune { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Owner name as written in the legacy register. Kept so it can be matched to a Person
    /// once people are imported from AD; not used as the real assignment.
    /// </summary>
    public string? LegacyAssignedTo { get; set; }

    public AssetStatus Status { get; private set; } = AssetStatus.ReadyToDeploy;

    public string DisplayName => AssetTag is null ? $"{Manufacturer} {Model} ({SerialNumber})" : $"{AssetTag} {Manufacturer} {Model}";

    /// <summary>
    /// Sets the starting status when migrating from a legacy register. Only valid before the asset has been
    /// through the lifecycle; every later change must go through a lifecycle action so it lands on the timeline.
    /// </summary>
    public void SetStatusFromMigration(AssetStatus status)
    {
        if (UpdatedAt is not null)
            throw new DomainException($"Asset {SerialNumber} is already tracked; use a lifecycle action.");

        Status = status;
    }

    // ---- Timeline entries for how the asset entered GIIM -------------------------------------------------

    public AssetEvent Imported(ActionContext context, string source) =>
        NewEvent(AssetEventType.Imported, context, null, Status, $"Imported from {source} as {Status}");

    public AssetEvent Created(ActionContext context) =>
        NewEvent(AssetEventType.Created, context, null, Status, $"Asset created as {Status}");

    // ---- Lifecycle actions: each validates, changes status and returns the timeline event ------------------

    public AssetEvent MarkReady(ActionContext context) =>
        Transition(AssetStatus.ReadyToDeploy, AssetEventType.MarkedReady, context, "Marked ready to deploy");

    /// <param name="method">How the data was removed, e.g. "Intune wipe", "Autopilot reset", "Reimaged".</param>
    public AssetEvent MarkWiped(ActionContext context, string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new DomainException("Record how the device was wiped.");

        return Transition(AssetStatus.Wiped, AssetEventType.Wiped, context, $"Data wiped ({method.Trim()})",
            new { Method = method.Trim() });
    }

    public AssetEvent ReportLost(ActionContext context, string circumstances, string? reportedBy) =>
        ReportMissing(AssetStatus.Lost, AssetEventType.ReportedLost, "Reported lost", context, circumstances, reportedBy, null);

    public AssetEvent ReportStolen(ActionContext context, string circumstances, string? reportedBy, string? policeReference) =>
        ReportMissing(AssetStatus.Stolen, AssetEventType.ReportedStolen, "Reported stolen", context, circumstances, reportedBy, policeReference);

    /// <summary>A lost or stolen device has been found. It comes back as Returned so it is wiped before reuse.</summary>
    public AssetEvent Recover(ActionContext context, string whereFound)
    {
        if (Status is not (AssetStatus.Lost or AssetStatus.Stolen))
            throw new DomainException($"{DisplayName} is {Status}, not lost or stolen.");
        if (string.IsNullOrWhiteSpace(whereFound))
            throw new DomainException("Record where or how the device was recovered.");

        return Transition(AssetStatus.Returned, AssetEventType.Recovered, context, $"Recovered ({whereFound.Trim()})",
            new { WhereFound = whereFound.Trim() });
    }

    public AssetEvent AddNote(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.Note))
            throw new DomainException("The note is empty.");

        return NewEvent(AssetEventType.NoteAdded, context, null, null, "Note added");
    }

    private AssetEvent ReportMissing(AssetStatus status, AssetEventType type, string summary, ActionContext context,
        string circumstances, string? reportedBy, string? policeReference)
    {
        if (string.IsNullOrWhiteSpace(circumstances))
            throw new DomainException("Describe the circumstances.");

        return Transition(status, type, context, summary, new
        {
            Circumstances = circumstances.Trim(),
            ReportedBy = string.IsNullOrWhiteSpace(reportedBy) ? null : reportedBy.Trim(),
            PoliceReference = string.IsNullOrWhiteSpace(policeReference) ? null : policeReference.Trim(),
        });
    }

    private AssetEvent Transition(AssetStatus next, AssetEventType type, ActionContext context, string summary, object? details = null)
    {
        if (!AssetLifecycle.CanTransition(Status, next))
            throw new DomainException($"{DisplayName} cannot go from {Status} to {next}.");

        var from = Status;
        var assetEvent = NewEvent(type, context, from, next, summary, details);
        Status = next;
        UpdatedAt = assetEvent.OccurredAt;
        return assetEvent;
    }

    private AssetEvent NewEvent(AssetEventType type, ActionContext context, AssetStatus? from, AssetStatus? to,
        string summary, object? details = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.Actor))
            throw new DomainException("Every action must record who performed it.");

        var ticket = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant();
        if (ticket?.Length > 50)
            throw new DomainException("Ticket number is too long (50 characters maximum).");

        var now = DateTimeOffset.UtcNow;
        var occurredAt = context.OccurredAt ?? now;
        if (occurredAt > now.AddMinutes(5))
            throw new DomainException("An action can't be recorded in the future.");

        return new AssetEvent
        {
            AssetId = Id,
            OccurredAt = occurredAt,
            RecordedAt = now,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            Actor = context.Actor.Trim(),
            TicketNumber = ticket,
            Summary = summary,
            Note = string.IsNullOrWhiteSpace(context.Note) ? null : context.Note.Trim(),
            DetailsJson = details is null ? null : System.Text.Json.JsonSerializer.Serialize(details),
        };
    }
}
