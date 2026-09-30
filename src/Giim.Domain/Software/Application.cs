using Giim.Domain.Common;

namespace Giim.Domain.Software;

public enum LicenceModel { Free, PerUser, PerDevice, Site }

/// <summary>
/// An app in the managed catalogue, i.e. one IT provisions and tracks.
/// Raw installs discovered by Intune are kept separately as inventory.
/// </summary>
public sealed class Application : Entity
{
    public required string Name { get; set; }
    public string? Vendor { get; set; }
    public LicenceModel LicenceModel { get; set; }
    public int? TotalSeats { get; set; }

    /// <summary>
    /// Security group that grants access (in Active Directory, or cloud-only in Entra); null when provisioning is
    /// manual. Groups synced from AD can only be changed in AD.
    /// </summary>
    public string? AccessGroupName { get; set; }
    public string? BusinessOwner { get; set; }
    public bool IsActive { get; set; } = true;
}
