using Giim.Domain.Common;

namespace Giim.Domain.Devices;

/// <summary>
/// GIIM's copy of a device as Intune last reported it. Intune is the source of these facts;
/// GIIM never edits them, it only refreshes them on each sync.
/// </summary>
public sealed class ManagedDevice : Entity
{
    public required string IntuneId { get; set; }
    public required string DeviceName { get; set; }

    /// <summary>Normalised the same way as asset serials; null when Intune has no usable serial.</summary>
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? OperatingSystem { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? ComplianceState { get; set; }
    public DateTimeOffset? LastSyncDateTime { get; set; }
    public DateTimeOffset? EnrolledDateTime { get; set; }

    /// <summary>Set when a full sync no longer returns the device (retired or deleted in Intune).</summary>
    public DateTimeOffset? RemovedFromIntuneAt { get; set; }
    public DateTimeOffset LastSeenBySyncAt { get; set; }

    /// <summary>Placeholder serials some hardware and VMs report instead of a real one.</summary>
    private static readonly HashSet<string> PlaceholderSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "0", "DEFAULTSTRING", "TOBEFILLEDBYO.E.M.", "SYSTEMSERIALNUMBER", "NONE", "N/A", "123456789",
    };

    public static bool IsPlaceholderSerial(string normalisedSerial) => PlaceholderSerials.Contains(normalisedSerial);
}
