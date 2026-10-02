using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Repairs;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Assets;

/// <summary>
/// Opens and completes repairs; the repair record, the asset's timeline entry and the email to the person who has the
/// device are saved together.
/// </summary>
public sealed class RepairService(GiimDbContext db, IOptions<ReminderOptions> reminders, IOptions<GiimOptions> giim)
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
        if (await HolderAsync(asset, cancellationToken) is { } facts)
            db.Notifications.Add(RepairEmails.Started(facts, repair, repair.OpenedAt));
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
        if (await HolderAsync(asset, cancellationToken) is { } facts)
            db.Notifications.Add(RepairEmails.Completed(facts, repair, repair.CompletedAt ?? DateTimeOffset.UtcNow));
        await SaveAsync(cancellationToken);
        return repair;
    }

    public async Task<IReadOnlyList<Repair>> ForAssetAsync(Guid assetId, CancellationToken cancellationToken) =>
        await db.Repairs.AsNoTracking().Where(r => r.AssetId == assetId).OrderByDescending(r => r.OpenedAt).ToListAsync(cancellationToken);

    /// <summary>Who to tell: the person the device is assigned to, if they're still here and have an address.</summary>
    private async Task<RepairEmailFacts?> HolderAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (!reminders.Value.RepairEmails || asset.AssignedToPersonId is not { } holderId) return null;
        var holder = await db.People.AsNoTracking().Where(p => p.Id == holderId && p.Status != PersonStatus.Left)
            .Select(p => new { p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken);
        if (holder?.Address is null) return null;
        var link = giim.Value.BaseUrl is { } b ? $"{b}/?asset={asset.Id}" : null;
        return new RepairEmailFacts($"{asset.Manufacturer} {asset.Model}", asset.AssetTag, holder.Address, holder.DisplayName, link);
    }

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
