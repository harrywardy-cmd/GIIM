namespace Giim.Connectors.Intune;

/// <summary>
/// Reads Intune managed devices. Read-only. Graph's managedDevices endpoint has no delta query,
/// so every sync is a full, paged read of the fields listed in <see cref="IntuneDevice"/>.
/// </summary>
public interface IIntuneClient
{
    IAsyncEnumerable<IntuneDevice> GetManagedDevicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Shape of a Graph managedDevice, limited to the fields GIIM requests with $select.</summary>
public sealed record IntuneDevice(
    string Id,
    string? DeviceName,
    string? SerialNumber,
    string? Manufacturer,
    string? Model,
    string? OperatingSystem,
    string? UserPrincipalName,
    string? ComplianceState,
    DateTimeOffset? LastSyncDateTime,
    DateTimeOffset? EnrolledDateTime);
