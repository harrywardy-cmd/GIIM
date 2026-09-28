using Giim.Domain.Common;

namespace Giim.Domain.Stock;

/// <summary>
/// Something without a serial number that IT only counts (laptop bags, mice, cables).
/// The quantity on hand is never stored; it is the sum of the item's <see cref="StockMovement"/>s.
/// </summary>
public sealed class StockItem : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>When total stock falls to or below this, the item is flagged as low.</summary>
    public int ReorderLevel { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsLow(int quantityOnHand) => quantityOnHand <= ReorderLevel;
}
