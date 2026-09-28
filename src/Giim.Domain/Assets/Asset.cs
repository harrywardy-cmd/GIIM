using Giim.Domain.Common;

namespace Giim.Domain.Assets;

public enum AssetCategory { Laptop, Desktop, Monitor, Dock, Phone, Tablet, Peripheral, Other }

public sealed class Asset : Entity
{
    public string? AssetTag { get; set; }
    public required string SerialNumber { get; set; }
    public required string Manufacturer { get; set; }
    public required string Model { get; set; }
    public AssetCategory Category { get; set; }
    public string? Location { get; set; }

    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? WarrantyExpiry { get; set; }
    public string? Supplier { get; set; }
    public decimal? Cost { get; set; }

    public string? IntuneDeviceId { get; set; }
    public DateTimeOffset? LastSeenInIntune { get; set; }
    public string? Notes { get; set; }

    public AssetStatus Status { get; private set; } = AssetStatus.InStock;

    public void ChangeStatus(AssetStatus next)
    {
        if (!AssetLifecycle.CanTransition(Status, next))
            throw new DomainException($"Asset {SerialNumber} cannot move from {Status} to {next}.");

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
