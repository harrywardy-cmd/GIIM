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

    /// <summary>Where the asset is; a managed location, so renaming the location updates every asset.</summary>
    public Guid? LocationId { get; set; }

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
    public static (Asset Asset, AssetEvent Event) Receive(NewAsset details, ActionContext context,
        AssetStatus startAs = AssetStatus.Received, Locations.Location? location = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        location?.EnsureActive();
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
            LocationId = location?.Id,
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
            Location = location?.Name,
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
    public AssetEvent Assign(ActionContext context, Guid personId, string personName, Locations.Location? location,
        IReadOnlyList<string> accessories, string? issuedWith = null)
    {
        ArgumentNullException.ThrowIfNull(accessories);
        if (Status != AssetStatus.ReadyToDeploy)
            throw new DomainException($"{DisplayName} is {Status}; only assets that are ready to deploy can be assigned.");
        location?.EnsureActive();

        var summary = issuedWith is null ? $"Assigned to {personName}" : $"Assigned to {personName} with {issuedWith}";
        var assetEvent = Transition(AssetStatus.Assigned, AssetEventType.Assigned, context, summary, new
        {
            PersonId = personId,
            PersonName = personName,
            Location = location?.Name,
            Accessories = accessories,
            IssuedWith = issuedWith,
        });

        AssignedToPersonId = personId;
        if (location is not null) LocationId = location.Id;
        return assetEvent;
    }

    /// <summary>Records that the asset has moved, e.g. between offices. No status change.</summary>
    public AssetEvent MoveTo(ActionContext context, Locations.Location location, string? previousLocationName)
    {
        ArgumentNullException.ThrowIfNull(location);
        location.EnsureActive();
        if (Status == AssetStatus.Disposed)
            throw new DomainException($"{DisplayName} has been disposed of.");
        if (LocationId == location.Id)
            throw new DomainException($"{DisplayName} is already at {location.Name}.");

        var assetEvent = NewEvent(AssetEventType.Moved, context, null, null,
            previousLocationName is null ? $"Moved to {location.Name}" : $"Moved from {previousLocationName} to {location.Name}",
            new { From = previousLocationName, To = location.Name });
        LocationId = location.Id;
        UpdatedAt = assetEvent.OccurredAt;
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

    // ---- Repair -----------------------------------------------------------------------------------------------

    public AssetEvent SendToRepair(ActionContext context, Repairs.Repair repair)
    {
        ArgumentNullException.ThrowIfNull(repair);
        if (repair.AssetId != Id)
            throw new DomainException("That repair belongs to a different asset.");

        var where = repair.Vendor is null ? "internal repair" : repair.WarrantyClaim ? $"{repair.Vendor} (warranty)" : repair.Vendor;
        return Transition(AssetStatus.InRepair, AssetEventType.RepairStarted, context, $"Sent to {where}: {repair.Fault}", new
        {
            RepairId = repair.Id,
            repair.Fault,
            repair.Vendor,
            repair.WarrantyClaim,
            repair.VendorReference,
        });
    }

    /// <summary>
    /// Repaired devices go back to the stage they came from (their user, Returned so they still get wiped,
    /// Received, or Ready to deploy). Beyond-repair devices stay in repair until they are returned and retired.
    /// </summary>
    public AssetEvent CompleteRepair(ActionContext context, Repairs.Repair repair)
    {
        ArgumentNullException.ThrowIfNull(repair);
        if (repair.AssetId != Id || repair.Outcome is null)
            throw new DomainException("Complete the repair record first.");

        var details = new { RepairId = repair.Id, repair.Outcome, repair.Diagnosis, repair.WorkPerformed, repair.Cost };
        if (repair.Outcome == Repairs.RepairOutcome.BeyondRepair)
            return NewEvent(AssetEventType.RepairCompleted, context, null, null, "Beyond repair", details);

        var back = repair.StartedFromStatus switch
        {
            AssetStatus.Assigned => AssetStatus.Assigned,
            AssetStatus.Returned => AssetStatus.Returned,
            AssetStatus.Received => AssetStatus.Received,
            _ => AssetStatus.ReadyToDeploy,
        };
        if (back == AssetStatus.Assigned && AssignedToPersonId is null)
            back = AssetStatus.Returned;  // the holder was unlinked meanwhile; treat as returned so it gets wiped

        return Transition(back, AssetEventType.RepairCompleted, context, $"Repaired: {repair.WorkPerformed}", details);
    }

    // ---- End of life ---------------------------------------------------------------------------------------

    public DateTimeOffset? RetiredAt { get; private set; }
    public string? RetirementReason { get; private set; }
    public DataSanitisation? DataSanitisation { get; private set; }
    public DateOnly? DisposedOn { get; private set; }
    public DisposalMethod? DisposalMethod { get; private set; }
    public string? DisposalCompany { get; private set; }
    public string? DisposalCertificate { get; private set; }

    public AssetEvent Retire(ActionContext context, string reason, DataSanitisation sanitisation, Locations.Location? finalLocation)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Give a reason for retiring the asset.");
        if (AssignedToPersonId is not null)
            throw new DomainException($"{DisplayName} is still recorded against a person; return it first so its accessories are checked.");

        var missing = Status is AssetStatus.Lost or AssetStatus.Stolen;
        if (missing && sanitisation is not (Assets.DataSanitisation.RemoteWipe or Assets.DataSanitisation.NotPossible))
            throw new DomainException("A lost or stolen device can only have been remote-wiped, or not wiped at all.");
        if (!missing && sanitisation is Assets.DataSanitisation.NotPossible or Assets.DataSanitisation.RemoteWipe)
            throw new DomainException("The device is in IT's hands: wipe it, destroy the drive, or confirm it has no storage.");

        var assetEvent = Transition(AssetStatus.Retired, AssetEventType.Retired, context, $"Retired: {reason.Trim()}", new
        {
            Reason = reason.Trim(),
            DataSanitisation = sanitisation,
            FinalLocation = finalLocation?.Name,
        });

        RetiredAt = assetEvent.OccurredAt;
        RetirementReason = reason.Trim();
        DataSanitisation = sanitisation;
        if (finalLocation is not null) LocationId = finalLocation.Id;
        return assetEvent;
    }

    public AssetEvent RecordDisposal(ActionContext context, DisposalMethod method, string? company, string? certificateNumber, DateOnly disposedOn)
    {
        var auditable = method is Assets.DisposalMethod.EWasteRecycling or Assets.DisposalMethod.Destroyed;
        if (auditable && string.IsNullOrWhiteSpace(company))
            throw new DomainException("Record the disposal company.");
        if (auditable && string.IsNullOrWhiteSpace(certificateNumber))
            throw new DomainException("Record the disposal or destruction certificate number; it is needed for audits.");
        if (disposedOn > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("Disposal date can't be in the future.");

        var assetEvent = Transition(AssetStatus.Disposed, AssetEventType.Disposed, context, $"Disposed ({Describe(method)})", new
        {
            Method = method,
            Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim(),
            Certificate = string.IsNullOrWhiteSpace(certificateNumber) ? null : certificateNumber.Trim(),
            DisposedOn = disposedOn,
        });

        DisposalMethod = method;
        DisposalCompany = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        DisposalCertificate = string.IsNullOrWhiteSpace(certificateNumber) ? null : certificateNumber.Trim();
        DisposedOn = disposedOn;
        return assetEvent;
    }

    private static string Describe(DisposalMethod method) => method switch
    {
        Assets.DisposalMethod.EWasteRecycling => "e-waste recycling",
        Assets.DisposalMethod.ReturnedToVendor => "returned to vendor",
        Assets.DisposalMethod.LeaseReturn => "lease return",
        _ => method.ToString().ToLowerInvariant(),
    };

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
