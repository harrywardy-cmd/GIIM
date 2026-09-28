using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Assets;

/// <summary>Raised when the asset changed after the user loaded it, so their action may no longer make sense.</summary>
public sealed class AssetChangedException(string message) : Exception(message);

/// <summary>Applies lifecycle actions and stores each one's timeline event in the same transaction.</summary>
public sealed class AssetLifecycleService(GiimDbContext db)
{
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

    public async Task<IReadOnlyList<AssetEvent>> GetTimelineAsync(Guid assetId, CancellationToken cancellationToken) =>
        await db.AssetEvents.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
}
