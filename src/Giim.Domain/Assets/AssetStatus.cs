namespace Giim.Domain.Assets;

public enum AssetStatus
{
    InStock,
    Assigned,
    ReturnRequested,
    Returned,
    Wiped,
    InRepair,   // warranty / RMA with vendor
    Lost,
    Disposed,
}

/// <summary>
/// The allowed hardware lifecycle. Every status change goes through here so an asset
/// can never skip a step, e.g. returning to stock without being wiped.
/// </summary>
public static class AssetLifecycle
{
    private static readonly Dictionary<AssetStatus, AssetStatus[]> Allowed = new()
    {
        [AssetStatus.InStock]         = [AssetStatus.Assigned, AssetStatus.InRepair, AssetStatus.Disposed],
        [AssetStatus.Assigned]        = [AssetStatus.ReturnRequested, AssetStatus.InRepair, AssetStatus.Lost],
        [AssetStatus.ReturnRequested] = [AssetStatus.Returned, AssetStatus.Lost],
        [AssetStatus.Returned]        = [AssetStatus.Wiped, AssetStatus.InRepair, AssetStatus.Disposed],
        [AssetStatus.Wiped]           = [AssetStatus.InStock, AssetStatus.Disposed],
        [AssetStatus.InRepair]        = [AssetStatus.InStock, AssetStatus.Assigned, AssetStatus.Disposed],
        [AssetStatus.Lost]            = [AssetStatus.Returned, AssetStatus.Disposed],
        [AssetStatus.Disposed]        = [],
    };

    public static bool CanTransition(AssetStatus from, AssetStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
}
