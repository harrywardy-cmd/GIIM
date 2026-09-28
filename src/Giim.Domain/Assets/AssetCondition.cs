namespace Giim.Domain.Assets;

/// <summary>Condition recorded when an asset comes back. Damaged or faulty kit should go to repair before reuse.</summary>
public enum AssetCondition
{
    Good,
    Fair,     // normal wear
    Damaged,  // physical damage, e.g. cracked screen
    Faulty,   // doesn't work properly
}
