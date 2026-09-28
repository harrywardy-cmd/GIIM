namespace Giim.Domain.Assets;

/// <summary>How the data on a device was dealt with before it left service. Required to retire an asset.</summary>
public enum DataSanitisation
{
    Wiped,           // wiped or reimaged by IT
    DriveDestroyed,  // storage removed and destroyed
    RemoteWipe,      // Intune remote wipe (lost or stolen devices)
    NoStorage,       // monitor, dock, peripheral: nothing to wipe
    NotPossible,     // lost or stolen with no remote wipe; recorded so the risk is visible
}

public enum DisposalMethod
{
    EWasteRecycling,
    Destroyed,
    ReturnedToVendor,
    LeaseReturn,
    Sold,
    Donated,
}
