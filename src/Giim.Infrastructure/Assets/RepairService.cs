using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Repairs;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Assets;

/// <summary>Opens and completes repairs; the repair record and the asset's timeline entry are saved together.</summary>
public sealed class RepairService(GiimDbContext db)
{
    public async Task<Repair> OpenAsync(Guid assetId, AssetStatus? expectedStatus, ActionContext context, string fault,
        string? vendor, bool warrantyClaim, string? vendorReference, DateOnly? sentOn, CancellationToken cancellationToken)
    {
        var asset = await LoadAsync(assetId, expectedStatus, cancellationToken);
        if (await db.Repairs.AnyAsync(r => r.AssetId == assetId && r.CompletedAt == null, cancellationToken))
            throw new DomainException($"{asset.DisplayName} already has an open repair.");

        var repair = Repair.Open(asset, context, fault, vendor, warrantyClaim, vendorReference, sentOn);
        db.AssetEvents.Add(asset.SendToRepair(context, repair));
        db.Repairs.Add(repair);
        await SaveAsync(cancellationToken);
        return repair;
    }

    public async Task<Repair> CompleteAsync(Guid assetId, Guid repairId, ActionContext context, RepairOutcome outcome,
        string? diagnosis, string? workPerformed, decimal? cost, CancellationToken cancellationToken)
    {
        var repair = await db.Repairs.FirstOrDefaultAsync(r => r.Id == repairId && r.AssetId == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Repair not found.");
        var asset = await LoadAsync(assetId, AssetStatus.InRepair, cancellationToken);

        repair.Complete(context, outcome, diagnosis, workPerformed, cost);
        db.AssetEvents.Add(asset.CompleteRepair(context, repair));
        await SaveAsync(cancellationToken);
        return repair;
    }

    public async Task<IReadOnlyList<Repair>> ForAssetAsync(Guid assetId, CancellationToken cancellationToken) =>
        await db.Repairs.AsNoTracking().Where(r => r.AssetId == assetId).OrderByDescending(r => r.OpenedAt).ToListAsync(cancellationToken);

    private async Task<Asset> LoadAsync(Guid assetId, AssetStatus? expectedStatus, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");
        if (expectedStatus is { } expected && asset.Status != expected)
            throw new AssetChangedException($"{asset.DisplayName} is now {asset.Status} (you saw {expected}). Refresh and try again.");
        return asset;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AssetChangedException("Someone else changed this asset at the same moment. Refresh and try again.");
        }
        catch (DbUpdateException e) when (e.InnerException?.Message.Contains("UX_Repairs_OneOpenPerAsset", StringComparison.Ordinal) == true)
        {
            throw new DomainException("This asset already has an open repair.");
        }
    }
}
