using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Assets;

/// <summary>Raised when the asset changed after the user loaded it, so their action may no longer make sense.</summary>
public sealed class AssetChangedException(string message) : Exception(message);

/// <summary>The serial number is already recorded; carries the existing asset so the user can open it instead.</summary>
public sealed class DuplicateAssetException(Guid existingAssetId, string message) : Exception(message)
{
    public Guid ExistingAssetId { get; } = existingAssetId;
}

/// <summary>Applies lifecycle actions and stores each one's timeline event in the same transaction.</summary>
public sealed class AssetLifecycleService(GiimDbContext db)
{
    public Task<Asset> CreateAsync(NewAsset details, ActionContext context, AssetStatus startAs, Guid? locationId,
        CancellationToken cancellationToken) =>
        CreateAsync(details, context, startAs, locationId, alongside: null, cancellationToken);

    /// <summary>
    /// As above, plus <paramref name="alongside"/>: more changes (such as marking the device request received) saved
    /// in the same save, so either both happen or neither does.
    /// </summary>
    public async Task<Asset> CreateAsync(NewAsset details, ActionContext context, AssetStatus startAs, Guid? locationId,
        Func<Asset, Task>? alongside, CancellationToken cancellationToken)
    {
        var category = await db.AssetCategories.FirstOrDefaultAsync(c => c.Id == details.CategoryId, cancellationToken)
            ?? throw new DomainException("Choose a category.");
        if (!category.IsActive)
            throw new DomainException($"The {category.Name} category is no longer in use.");
        var location = locationId is { } id
            ? await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw new DomainException("That location doesn't exist.")
            : null;

        var (asset, assetEvent) = Asset.Receive(details, context, startAs, location);

        if (await db.Assets.Where(a => a.SerialNumber == asset.SerialNumber).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
            throw new DuplicateAssetException(existing, $"Serial {asset.SerialNumber} is already recorded.");
        if (asset.AssetTag is not null && await db.Assets.AnyAsync(a => a.AssetTag == asset.AssetTag, cancellationToken))
            throw new DomainException($"Asset tag {asset.AssetTag} is already used on another device.");

        db.Assets.Add(asset);
        db.AssetEvents.Add(assetEvent);
        if (alongside is not null) await alongside(asset);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone added the same serial between our check and our save; the unique index stopped the duplicate.
            var winner = await db.Assets.AsNoTracking()
                .Where(a => a.SerialNumber == asset.SerialNumber && a.Id != asset.Id)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
            if (winner is null) throw;
            throw new DuplicateAssetException(winner.Value, $"Serial {asset.SerialNumber} was recorded by someone else a moment ago.");
        }

        return asset;
    }

    /// <summary>
    /// Exact match on serial number or asset tag, for barcode scanners and "type the serial, press Enter".
    /// Serials are normalised first, so spaces, dashes and case don't matter.
    /// </summary>
    public async Task<Guid?> LookupAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var serial = Domain.Importing.ImportNormalizer.Serial(code);
        var tag = code.Trim().ToUpperInvariant();
        return await db.Assets.AsNoTracking()
            .Where(a => a.SerialNumber == serial || a.AssetTag == tag)
            .OrderBy(a => a.SerialNumber == serial ? 0 : 1)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <param name="expectedStatus">
    /// The status the user saw when they chose the action. If someone else has changed the asset since,
    /// the action is refused rather than applied to a state the user never saw.
    /// </param>
    public async Task<AssetEvent> ApplyAsync(Guid assetId, AssetStatus? expectedStatus, Func<Asset, AssetEvent> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");

        if (expectedStatus is { } expected && asset.Status != expected)
            throw new AssetChangedException($"{asset.DisplayName} is now {asset.Status} (you saw {expected}). Refresh and try again.");

        var assetEvent = action(asset);
        db.AssetEvents.Add(assetEvent);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AssetChangedException($"{asset.DisplayName} was changed by someone else at the same moment. Refresh and try again.");
        }

        return assetEvent;
    }

    public async Task<AssetEvent> MoveAsync(Guid assetId, AssetStatus? expectedStatus, Guid locationId, ActionContext context,
        CancellationToken cancellationToken)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken)
            ?? throw new DomainException("That location doesn't exist.");
        var previous = await db.Assets.Where(a => a.Id == assetId)
            .Select(a => db.Locations.Where(l => l.Id == a.LocationId).Select(l => l.Name).FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken);

        return await ApplyAsync(assetId, expectedStatus, a => a.MoveTo(context, location, previous), cancellationToken);
    }

    /// <summary>Loads a location for an action that needs one (e.g. retire to the e-waste cage); null stays null.</summary>
    public async Task<Domain.Locations.Location?> FindLocationAsync(Guid? locationId, CancellationToken cancellationToken) =>
        locationId is { } id
            ? await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw new DomainException("That location doesn't exist.")
            : null;

    public async Task<IReadOnlyList<AssetEvent>> GetTimelineAsync(Guid assetId, CancellationToken cancellationToken) =>
        await db.AssetEvents.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
}
