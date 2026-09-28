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

    /// <summary>The owner's department as written in the legacy register; used to tell apart people who share a name.</summary>
    public string? LegacyDepartment { get; set; }

    /// <summary>Who holds the asset now. Kept in step with the active <c>Assignment</c> by the lifecycle actions.</summary>
    public Guid? AssignedToPersonId { get; private set; }

    /// <summary>
    /// Records who already holds an asset that came from the legacy register, once the "Assigned To" name has been
    /// matched to a real person. No status change: the device was already with them.
    /// </summary>
    public AssetEvent LinkLegacyOwner(ActionContext context, Guid personId, string personName, string matchedBy)
    {
        if (Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested))
            throw new DomainException($"{DisplayName} is {Status}; only assigned assets can be linked to an owner.");
        if (AssignedToPersonId is not null)
            throw new DomainException($"{DisplayName} is already linked to an owner.");

        AssignedToPersonId = personId;
        var assetEvent = NewEvent(AssetEventType.OwnerLinked, context, null, null,
            $"Linked to {personName} (from legacy register, matched by {matchedBy})",
            new { PersonId = personId, PersonName = personName, LegacyName = LegacyAssignedTo, MatchedBy = matchedBy });
        UpdatedAt = assetEvent.OccurredAt;
        return assetEvent;
    }

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

    // ---- Adding a new device by hand ---------------------------------------------------------------------

    /// <summary>
    /// Creates the record for a device that has just arrived. Serial and manufacturer are cleaned up the same way
    /// as imports, so a scanned "5cg 123-abc" matches Intune's "5CG123ABC".
    /// </summary>
    /// <param name="startAs">Received (needs setting up) or ReadyToDeploy (already set up, e.g. existing spare stock).</param>
    public static (Asset Asset, AssetEvent Event) Receive(NewAsset details, ActionContext context, AssetStatus startAs = AssetStatus.Received)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (startAs is not (AssetStatus.Received or AssetStatus.ReadyToDeploy))
            throw new DomainException("A new asset starts as Received or Ready to deploy.");

        var serial = Importing.ImportNormalizer.Serial(details.SerialNumber)
            ?? throw new DomainException("Serial number is required.");
        if (Devices.ManagedDevice.IsPlaceholderSerial(serial))
            throw new DomainException($"'{serial}' is a placeholder, not a real serial number. Check the label on the device.");
        if (serial.Length > 100)
            throw new DomainException("Serial number is too long.");

        var manufacturer = Importing.ImportNormalizer.Manufacturer(details.Manufacturer)
            ?? throw new DomainException("Manufacturer is required.");
        if (string.IsNullOrWhiteSpace(details.Model))
            throw new DomainException("Model is required.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (details.PurchaseDate > today)
            throw new DomainException("Purchase date can't be in the future.");
        if (details.PurchaseDate is { } bought && details.WarrantyExpiry < bought)
            throw new DomainException("Warranty can't expire before the purchase date.");
        if (details.Cost < 0)
            throw new DomainException("Cost can't be negative.");

        var asset = new Asset
        {
            SerialNumber = serial,
            AssetTag = string.IsNullOrWhiteSpace(details.AssetTag) ? null : details.AssetTag.Trim().ToUpperInvariant(),
            Manufacturer = manufacturer,
            Model = details.Model.Trim(),
            CategoryId = details.CategoryId,
            Location = string.IsNullOrWhiteSpace(details.Location) ? null : details.Location.Trim(),
            PurchaseDate = details.PurchaseDate,
            WarrantyExpiry = details.WarrantyExpiry,
            Supplier = string.IsNullOrWhiteSpace(details.Supplier) ? null : details.Supplier.Trim(),
            Cost = details.Cost,
            Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes.Trim(),
            Status = startAs,
        };

        var summary = startAs == AssetStatus.Received ? "Received" : "Added as ready to deploy";
        var assetEvent = asset.NewEvent(AssetEventType.Created, context, null, startAs, summary, new
        {
            asset.Location,
            asset.Supplier,
            PurchaseOrder = string.IsNullOrWhiteSpace(details.PurchaseOrder) ? null : details.PurchaseOrder.Trim(),
        });
        return (asset, assetEvent);
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

    // ---- Issue and return ------------------------------------------------------------------------------------

    /// <param name="issuedWith">Set when this asset is an accessory issued with another asset, e.g. a dock with a laptop.</param>
    public AssetEvent Assign(ActionContext context, Guid personId, string personName, string? location,
        IReadOnlyList<string> accessories, string? issuedWith = null)
    {
        ArgumentNullException.ThrowIfNull(accessories);
        if (Status != AssetStatus.ReadyToDeploy)
            throw new DomainException($"{DisplayName} is {Status}; only assets that are ready to deploy can be assigned.");

        var summary = issuedWith is null ? $"Assigned to {personName}" : $"Assigned to {personName} with {issuedWith}";
        var assetEvent = Transition(AssetStatus.Assigned, AssetEventType.Assigned, context, summary, new
        {
            PersonId = personId,
            PersonName = personName,
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Accessories = accessories,
            IssuedWith = issuedWith,
        });

        AssignedToPersonId = personId;
        if (!string.IsNullOrWhiteSpace(location)) Location = location.Trim();
        return assetEvent;
    }

    /// <summary>Asks for the asset back, e.g. when the holder is leaving. The asset stays with them until returned.</summary>
    public AssetEvent RequestReturn(ActionContext context, DateOnly? dueDate) =>
        Transition(AssetStatus.ReturnRequested, AssetEventType.ReturnRequested, context,
            dueDate is null ? "Return requested" : $"Return requested by {dueDate:d MMM yyyy}",
            new { DueDate = dueDate, PersonId = AssignedToPersonId });

    /// <param name="missing">Accessories that should have come back with the asset but didn't.</param>
    public AssetEvent Return(ActionContext context, AssetCondition condition, string? returnedBy, string? previousHolder,
        IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);

        var summary = $"Returned ({condition.ToString().ToLowerInvariant()})"
            + (previousHolder is null ? "" : $" from {previousHolder}")
            + (missing.Count == 0 ? "" : $"; missing: {string.Join(", ", missing)}");

        var assetEvent = Transition(AssetStatus.Returned, AssetEventType.Returned, context, summary, new
        {
            Condition = condition,
            ReturnedBy = string.IsNullOrWhiteSpace(returnedBy) ? null : returnedBy.Trim(),
            PreviousHolderId = AssignedToPersonId,
            PreviousHolder = previousHolder ?? LegacyAssignedTo,
            Missing = missing,
        });

        AssignedToPersonId = null;
        return assetEvent;
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
