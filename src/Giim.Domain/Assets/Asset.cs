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

    public AssetStatus Status { get; private set; } = AssetStatus.InStock;

    /// <summary>
    /// Sets the starting status when migrating from a legacy register. Only valid before the
    /// asset has been through the lifecycle; every later change must go through <see cref="ChangeStatus"/>.
    /// </summary>
    public void SetStatusFromMigration(AssetStatus status)
    {
        if (UpdatedAt is not null)
            throw new DomainException($"Asset {SerialNumber} is already tracked; use ChangeStatus.");

        Status = status;
    }

    public void ChangeStatus(AssetStatus next)
    {
        if (!AssetLifecycle.CanTransition(Status, next))
            throw new DomainException($"Asset {SerialNumber} cannot move from {Status} to {next}.");

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
