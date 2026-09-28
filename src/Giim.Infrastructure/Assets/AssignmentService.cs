using System.Data;
using Giim.Domain.Assets;
using Giim.Domain.Assignments;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Stock;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Assets;

/// <summary>
/// One accessory to issue: another tracked asset (<see cref="AssetId"/>), a stock item (<see cref="StockItemId"/>,
/// optionally taken from stock at <see cref="TakeFromStockAt"/>), or free text (<see cref="Description"/>).
/// </summary>
public sealed record AccessoryRequest(Guid? AssetId, Guid? StockItemId, string? Description, int Quantity = 1, string? TakeFromStockAt = null);

/// <summary>
/// Issues and returns assets with their accessories. Each call is all-or-nothing: the asset, its accessories,
/// the stock movements and every timeline entry are saved in one transaction, or none of them are.
/// </summary>
public sealed class AssignmentService(GiimDbContext db)
{
    public Task<Assignment> AssignAsync(Guid assetId, Guid personId, AssetStatus? expectedStatus, ActionContext context,
        string? location, IReadOnlyList<AccessoryRequest> accessories, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessories);

        return InTransactionAsync(async () =>
        {
            var asset = await LoadAssetAsync(assetId, expectedStatus, cancellationToken);
            var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
                ?? throw new KeyNotFoundException("Person not found.");
            if (person.Status == PersonStatus.Left)
                throw new DomainException($"{person.DisplayName} has left the organisation; assets can't be assigned to them.");

            var assignment = Assignment.Start(person.Id, asset.Id, context);

            foreach (var request in accessories)
                assignment.Accessories.Add(await IssueAccessoryAsync(request, asset, person, context, location, cancellationToken));

            db.AssetEvents.Add(asset.Assign(context, person.Id, person.DisplayName, location,
                [.. assignment.Accessories.Select(a => a.Label)]));
            db.Assignments.Add(assignment);
            return assignment;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<AccessoryLine>> ReturnAsync(Guid assetId, AssetStatus? expectedStatus, ActionContext context,
        AssetCondition condition, string? returnedBy, IReadOnlySet<Guid> returnedAccessoryIds, string? returnStockTo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(returnedAccessoryIds);

        return InTransactionAsync<IReadOnlyList<AccessoryLine>>(async () =>
        {
            var asset = await LoadAssetAsync(assetId, expectedStatus, cancellationToken);
            var holderName = asset.AssignedToPersonId is { } holderId
                ? await db.People.Where(p => p.Id == holderId).Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken)
                : null;

            // Legacy assets that were never linked to a person have no assignment; they can still be returned.
            var assignment = await db.Assignments.Include(a => a.Accessories)
                .FirstOrDefaultAsync(a => a.AssetId == assetId && a.EndedAt == null, cancellationToken);
            if (assignment is null && returnedAccessoryIds.Count > 0)
                throw new DomainException("This asset has no recorded accessories.");

            var missing = assignment?.Complete(context, condition, returnedBy, returnedAccessoryIds) ?? [];

            foreach (var line in assignment?.Accessories.Where(a => a.Status == AccessoryStatus.Returned) ?? [])
            {
                if (line.AccessoryAssetId is { } accessoryId)
                    await ReturnAccessoryAssetAsync(accessoryId, asset, assignment!.PersonId, holderName, condition, returnedBy, context, cancellationToken);
                else if (line.StockItemId is { } stockItemId && !string.IsNullOrWhiteSpace(returnStockTo))
                    await MoveStockAsync(stockItemId, returnStockTo, StockMovementReason.Returned, line.Quantity, context,
                        $"Returned with {asset.DisplayName}", cancellationToken);
            }

            db.AssetEvents.Add(asset.Return(context, condition, returnedBy, holderName, [.. missing.Select(m => m.Label)]));
            return missing;
        }, cancellationToken);
    }

    private async Task<AccessoryLine> IssueAccessoryAsync(AccessoryRequest request, Asset mainAsset, Person person,
        ActionContext context, string? location, CancellationToken cancellationToken)
    {
        if (request.Quantity < 1)
            throw new DomainException("Accessory quantity must be at least 1.");

        if (request.AssetId is { } accessoryId)
        {
            if (accessoryId == mainAsset.Id)
                throw new DomainException("An asset can't be its own accessory.");

            var accessory = await db.Assets.FirstOrDefaultAsync(a => a.Id == accessoryId, cancellationToken)
                ?? throw new KeyNotFoundException("Accessory asset not found.");
            db.AssetEvents.Add(accessory.Assign(context, person.Id, person.DisplayName, location, [], issuedWith: mainAsset.DisplayName));
            db.Assignments.Add(Assignment.Start(person.Id, accessory.Id, context, $"Issued with {mainAsset.DisplayName}"));
            return new AccessoryLine { Description = accessory.DisplayName, AccessoryAssetId = accessory.Id };
        }

        if (request.StockItemId is { } stockItemId)
        {
            var item = await db.StockItems.FirstOrDefaultAsync(s => s.Id == stockItemId, cancellationToken)
                ?? throw new KeyNotFoundException("Stock item not found.");
            if (!string.IsNullOrWhiteSpace(request.TakeFromStockAt))
                await MoveStockAsync(item.Id, request.TakeFromStockAt, StockMovementReason.Issued, request.Quantity, context,
                    $"Issued to {person.DisplayName} with {mainAsset.DisplayName}", cancellationToken);
            return new AccessoryLine { Description = item.Name, StockItemId = item.Id, Quantity = request.Quantity };
        }

        if (string.IsNullOrWhiteSpace(request.Description))
            throw new DomainException("Describe the accessory, or choose an asset or stock item.");
        return new AccessoryLine { Description = request.Description.Trim(), Quantity = request.Quantity };
    }

    private async Task ReturnAccessoryAssetAsync(Guid accessoryId, Asset mainAsset, Guid holderId, string? holderName,
        AssetCondition condition, string? returnedBy, ActionContext context, CancellationToken cancellationToken)
    {
        var accessory = await db.Assets.FirstAsync(a => a.Id == accessoryId, cancellationToken);
        // Only return it if it is still with the same person; it may have been returned or reissued separately.
        if (accessory.AssignedToPersonId != holderId || accessory.Status is not (AssetStatus.Assigned or AssetStatus.ReturnRequested))
            return;

        var accessoryAssignment = await db.Assignments.Include(a => a.Accessories)
            .FirstOrDefaultAsync(a => a.AssetId == accessoryId && a.EndedAt == null, cancellationToken);
        accessoryAssignment?.Complete(context, condition, returnedBy, new HashSet<Guid>());
        db.AssetEvents.Add(accessory.Return(context with { Note = $"Returned with {mainAsset.DisplayName}" }, condition, returnedBy, holderName, []));
    }

    private async Task MoveStockAsync(Guid stockItemId, string location, StockMovementReason reason, int quantity,
        ActionContext context, string note, CancellationToken cancellationToken)
    {
        var item = await db.StockItems.FirstAsync(s => s.Id == stockItemId, cancellationToken);
        var trimmed = location.Trim();
        var current = await db.StockMovements.Where(m => m.StockItemId == stockItemId && m.Location == trimmed)
            .SumAsync(m => m.Quantity, cancellationToken)
            + db.StockMovements.Local.Where(m => m.StockItemId == stockItemId && m.Location == trimmed).Sum(m => m.Quantity);
        db.StockMovements.Add(StockMovement.Record(item, trimmed, reason, quantity, current, context.Actor, note, context.TicketNumber));
    }

    private async Task<Asset> LoadAssetAsync(Guid assetId, AssetStatus? expectedStatus, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");
        if (expectedStatus is { } expected && asset.Status != expected)
            throw new AssetChangedException($"{asset.DisplayName} is now {asset.Status} (you saw {expected}). Refresh and try again.");
        return asset;
    }

    /// <summary>
    /// Serializable so stock levels checked here can't be changed by someone else before we save; the execution
    /// strategy re-runs the whole unit if SQL Server picks it as a deadlock victim.
    /// </summary>
    private Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new AssetChangedException("Someone else changed this asset at the same moment. Refresh and try again.");
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
}
