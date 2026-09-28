using Giim.Domain.Common;

namespace Giim.Domain.Locations;

public enum LocationKind { Office, ItStoreRoom, DistributionCentre, Site, Remote, Other }

/// <summary>
/// A managed place where assets and stock can be. Assets and stock point at a location rather than holding
/// its name, so renaming a location updates everything, and "Melb office" can't become a second Melbourne.
/// </summary>
public sealed class Location : Entity
{
    public const int MaxNameLength = 150;

    public required string Name { get; set; }
    public LocationKind Kind { get; set; } = LocationKind.Other;
    public string? Address { get; set; }

    /// <summary>Offered as a place to hold stock (IT store rooms, warehouses).</summary>
    public bool HoldsStock { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Trims and collapses repeated spaces, so "  IT  Store Room " and "IT Store Room" are the same name.</summary>
    public static string CleanName(string? name)
    {
        var cleaned = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (cleaned.Length == 0) throw new DomainException("A location needs a name.");
        if (cleaned.Length > MaxNameLength) throw new DomainException($"Location names are limited to {MaxNameLength} characters.");
        return cleaned;
    }

    /// <summary>Best guess for locations created from imported data; IT can correct it on the Locations page.</summary>
    public static LocationKind GuessKind(string name) => name.ToUpperInvariant() switch
    {
        var n when n.Contains("STORE", StringComparison.Ordinal) || n.Contains("STOCK", StringComparison.Ordinal) => LocationKind.ItStoreRoom,
        var n when n.Contains("DISTRIBUTION", StringComparison.Ordinal) || n.Contains("WAREHOUSE", StringComparison.Ordinal) => LocationKind.DistributionCentre,
        var n when n.Contains("OFFICE", StringComparison.Ordinal) => LocationKind.Office,
        var n when n.Contains("HOME", StringComparison.Ordinal) || n.Contains("REMOTE", StringComparison.Ordinal) => LocationKind.Remote,
        _ => LocationKind.Other,
    };

    public void EnsureActive()
    {
        if (!IsActive) throw new DomainException($"{Name} is no longer in use; choose another location.");
    }
}
