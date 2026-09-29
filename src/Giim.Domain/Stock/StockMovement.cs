using Giim.Domain.Common;

namespace Giim.Domain.Stock;

public enum StockMovementReason
{
    Received,    // delivery from a supplier            (+)
    Issued,      // handed out to staff                 (-)
    Returned,    // came back into the store            (+)
    WrittenOff,  // damaged, lost, disposed             (-)
    Adjustment,  // stocktake correction                (+ or -)
}

/// <summary>One entry in the stock ledger. Entries are never edited; mistakes are fixed with an Adjustment.</summary>
public sealed class StockMovement : Entity
{
    public Guid StockItemId { get; init; }

    /// <summary>The managed location; levels are counted per location.</summary>
    public Guid LocationId { get; init; }

    /// <summary>The location's name when the movement was recorded, kept as written for the audit trail.</summary>
    public required string Location { get; init; }

    /// <summary>Signed change: positive adds stock, negative removes it.</summary>
    public int Quantity { get; init; }
    public StockMovementReason Reason { get; init; }

    public string? Note { get; init; }
    public string? ServiceDeskRequestId { get; init; }
    public required string Actor { get; init; }

    /// <summary>
    /// Records a movement. <paramref name="quantity"/> is always entered as a positive count except for
    /// Adjustment, which is signed; the reason decides the direction so a typo can't reverse a delivery.
    /// </summary>
    public static StockMovement Record(
        StockItem item, Locations.Location location, StockMovementReason reason, int quantity, int currentLevelAtLocation,
        string actor, string? note = null, string? serviceDeskRequestId = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(location);

        // A closed location can still be emptied (issued, written off, adjusted) but can't take new deliveries.
        if (reason == StockMovementReason.Received)
            location.EnsureActive();
        if (quantity == 0)
            throw new DomainException("Quantity cannot be zero.");
        if (reason != StockMovementReason.Adjustment && quantity < 0)
            throw new DomainException($"Enter a positive quantity for {reason}; only Adjustment can be negative.");
        if (reason == StockMovementReason.Adjustment && string.IsNullOrWhiteSpace(note))
            throw new DomainException("An adjustment needs a note explaining why.");

        var signed = reason switch
        {
            StockMovementReason.Issued or StockMovementReason.WrittenOff => -quantity,
            _ => quantity,
        };

        if (currentLevelAtLocation + signed < 0)
            throw new DomainException(
                $"Only {currentLevelAtLocation} {item.Name} at {location.Name}; cannot remove {Math.Abs(signed)}.");

        return new StockMovement
        {
            StockItemId = item.Id,
            LocationId = location.Id,
            Location = location.Name,
            Quantity = signed,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            // Upper-case like every other ticket reference, so "inc123" and "INC123" are the same ticket.
            ServiceDeskRequestId = string.IsNullOrWhiteSpace(serviceDeskRequestId) ? null : serviceDeskRequestId.Trim().ToUpperInvariant(),
            Actor = actor,
        };
    }
}
