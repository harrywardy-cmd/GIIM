using System.Data;
using System.Text.Json;
using Giim.Domain.Auditing;
using Giim.Domain.Stock;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Stock;

public sealed record LocationLevel(string Location, int Quantity);

public sealed record StockLevel(
    Guid Id, string Name, string? Description, int ReorderLevel, bool IsActive,
    int Total, bool IsLow, IReadOnlyList<LocationLevel> Locations);

/// <summary>Stock levels are always calculated from the movement ledger, so they can't drift out of sync.</summary>
public sealed class StockService(GiimDbContext db)
{
    public async Task<IReadOnlyList<StockLevel>> GetLevelsAsync(CancellationToken cancellationToken)
    {
        var items = await db.StockItems.AsNoTracking().OrderBy(i => i.Name).ToListAsync(cancellationToken);
        var sums = await db.StockMovements
            .GroupBy(m => new { m.StockItemId, m.Location })
            .Select(g => new { g.Key.StockItemId, g.Key.Location, Quantity = g.Sum(m => m.Quantity) })
            .ToListAsync(cancellationToken);

        return items.Select(item =>
        {
            var locations = sums.Where(s => s.StockItemId == item.Id && s.Quantity != 0)
                .OrderBy(s => s.Location)
                .Select(s => new LocationLevel(s.Location, s.Quantity))
                .ToList();
            var total = locations.Sum(l => l.Quantity);
            return new StockLevel(item.Id, item.Name, item.Description, item.ReorderLevel, item.IsActive,
                total, item.IsActive && item.IsLow(total), locations);
        }).ToList();
    }

    public async Task<IReadOnlyList<StockMovement>> GetHistoryAsync(Guid stockItemId, CancellationToken cancellationToken) =>
        await db.StockMovements.AsNoTracking()
            .Where(m => m.StockItemId == stockItemId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

    public async Task<StockMovement> RecordAsync(
        Guid stockItemId, string location, StockMovementReason reason, int quantity,
        string actor, string? note, string? serviceDeskRequestId, CancellationToken cancellationToken)
    {
        var item = await db.StockItems.FindAsync([stockItemId], cancellationToken)
            ?? throw new KeyNotFoundException("Stock item not found.");
        var trimmedLocation = location.Trim();

        // Serializable: two people issuing the last item at the same moment can't both succeed.
        // The retry strategy re-runs the block if SQL Server picks this transaction as a deadlock victim.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var current = await db.StockMovements
                .Where(m => m.StockItemId == stockItemId && m.Location == trimmedLocation)
                .SumAsync(m => m.Quantity, cancellationToken);

            var movement = StockMovement.Record(item, trimmedLocation, reason, quantity, current, actor, note, serviceDeskRequestId);

            db.StockMovements.Add(movement);
            db.AuditEntries.Add(new AuditEntry
            {
                Actor = actor,
                Action = $"Stock{reason}",
                EntityType = nameof(StockItem),
                EntityId = item.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new { item.Name, movement.Location, movement.Quantity, movement.Note, movement.ServiceDeskRequestId }),
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return movement;
        });
    }
}
