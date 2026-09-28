namespace Giim.Domain.Assets;

public enum AssetStatus
{
    Received,        // delivered, not yet set up
    ReadyToDeploy,   // set up and available to issue ("Available")
    Assigned,
    ReturnRequested, // offboarding: waiting for the device to come back
    Returned,
    Wiped,           // data removed; required before a returned device is reused
    InRepair,        // internal repair, or warranty / RMA with vendor
    Lost,
    Stolen,
    Retired,         // out of service; waiting for disposal
    Disposed,        // final
}

/// <summary>
/// The allowed hardware lifecycle. Every status change goes through here so an asset can never skip a step,
/// e.g. a returned device must be wiped before it can be deployed again.
/// </summary>
public static class AssetLifecycle
{
    private static readonly Dictionary<AssetStatus, AssetStatus[]> Allowed = new()
    {
        [AssetStatus.Received]        = [AssetStatus.ReadyToDeploy, AssetStatus.InRepair, AssetStatus.Retired],
        [AssetStatus.ReadyToDeploy]   = [AssetStatus.Assigned, AssetStatus.InRepair, AssetStatus.Lost, AssetStatus.Stolen, AssetStatus.Retired],
        [AssetStatus.Assigned]        = [AssetStatus.ReturnRequested, AssetStatus.Returned, AssetStatus.InRepair, AssetStatus.Lost, AssetStatus.Stolen],
        [AssetStatus.ReturnRequested] = [AssetStatus.Returned, AssetStatus.Lost, AssetStatus.Stolen],
        [AssetStatus.Returned]        = [AssetStatus.Wiped, AssetStatus.InRepair, AssetStatus.Lost, AssetStatus.Stolen, AssetStatus.Retired],
        [AssetStatus.Wiped]           = [AssetStatus.ReadyToDeploy, AssetStatus.InRepair, AssetStatus.Retired],
        // A repaired device goes back to the stage it came from, so repair can never skip a wipe or set-up step.
        [AssetStatus.InRepair]        = [AssetStatus.ReadyToDeploy, AssetStatus.Assigned, AssetStatus.Returned, AssetStatus.Received, AssetStatus.Retired],
        // Found or recovered devices come back through Returned, so they are wiped before reuse.
        [AssetStatus.Lost]            = [AssetStatus.Returned, AssetStatus.Retired],
        [AssetStatus.Stolen]          = [AssetStatus.Returned, AssetStatus.Retired],
        [AssetStatus.Retired]         = [AssetStatus.Disposed],
        [AssetStatus.Disposed]        = [],
    };

    public static bool CanTransition(AssetStatus from, AssetStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<AssetStatus> NextStatuses(AssetStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Statuses in which nobody should be actively using the device.</summary>
    public static bool IsOutOfUse(AssetStatus status) => status is AssetStatus.Received or AssetStatus.ReadyToDeploy
        or AssetStatus.Returned or AssetStatus.Wiped or AssetStatus.Lost or AssetStatus.Stolen
        or AssetStatus.Retired or AssetStatus.Disposed;
}
