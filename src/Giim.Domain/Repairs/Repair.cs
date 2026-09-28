using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Repairs;

public enum RepairOutcome
{
    Repaired,
    BeyondRepair,   // not economical or not possible to repair; the device should be retired
}

/// <summary>
/// One repair of one asset: internal (Vendor is null) or with a vendor, possibly under warranty.
/// Opened with the fault; completed with the diagnosis, work done and cost.
/// </summary>
public sealed class Repair : Entity
{
    public Guid AssetId { get; init; }

    /// <summary>The status the asset was in before repair; it goes back there when repaired.</summary>
    public AssetStatus StartedFromStatus { get; init; }

    public required string Fault { get; init; }
    public string? Vendor { get; init; }
    public bool WarrantyClaim { get; init; }

    /// <summary>The vendor's case or RMA number.</summary>
    public string? VendorReference { get; init; }
    public DateOnly? SentOn { get; init; }
    public required string OpenedBy { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public string? TicketNumber { get; init; }

    public string? Diagnosis { get; private set; }
    public string? WorkPerformed { get; private set; }
    public decimal? Cost { get; private set; }
    public RepairOutcome? Outcome { get; private set; }
    public string? CompletedBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsOpen => CompletedAt is null;
    public TimeSpan? Duration => CompletedAt - OpenedAt;

    public static Repair Open(Asset asset, ActionContext context, string fault, string? vendor, bool warrantyClaim,
        string? vendorReference, DateOnly? sentOn)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(fault))
            throw new DomainException("Describe the fault.");
        if (warrantyClaim && string.IsNullOrWhiteSpace(vendor))
            throw new DomainException("A warranty claim needs the vendor it was sent to.");

        return new Repair
        {
            AssetId = asset.Id,
            StartedFromStatus = asset.Status,
            Fault = fault.Trim(),
            Vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim(),
            WarrantyClaim = warrantyClaim,
            VendorReference = string.IsNullOrWhiteSpace(vendorReference) ? null : vendorReference.Trim(),
            SentOn = sentOn,
            OpenedBy = context.Actor,
            OpenedAt = context.OccurredAt ?? DateTimeOffset.UtcNow,
            TicketNumber = string.IsNullOrWhiteSpace(context.TicketNumber) ? null : context.TicketNumber.Trim().ToUpperInvariant(),
        };
    }

    public void Complete(ActionContext context, RepairOutcome outcome, string? diagnosis, string? workPerformed, decimal? cost)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsOpen)
            throw new DomainException("This repair has already been completed.");
        if (outcome == RepairOutcome.Repaired && string.IsNullOrWhiteSpace(workPerformed))
            throw new DomainException("Record what was repaired.");
        if (cost < 0)
            throw new DomainException("Cost can't be negative.");

        Outcome = outcome;
        Diagnosis = string.IsNullOrWhiteSpace(diagnosis) ? null : diagnosis.Trim();
        WorkPerformed = string.IsNullOrWhiteSpace(workPerformed) ? null : workPerformed.Trim();
        Cost = cost;
        CompletedBy = context.Actor;
        CompletedAt = context.OccurredAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = CompletedAt;
    }
}
