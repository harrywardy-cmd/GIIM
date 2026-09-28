namespace Giim.Connectors.Intune;

/// <summary>Microsoft Graph: Intune managed devices. Read-only in Phase 1.</summary>
public interface IIntuneClient
{
    /// <summary>
    /// Returns devices changed since <paramref name="deltaToken"/> (all devices when null),
    /// so a sync of 150k devices only fetches what changed.
    /// </summary>
    Task<IntuneDeltaPage> GetManagedDevicesAsync(string? deltaToken, CancellationToken cancellationToken = default);
}

public sealed record IntuneDeltaPage(IReadOnlyList<IntuneDevice> Devices, string NextDeltaToken);

public sealed record IntuneDevice(
    string Id,
    string DeviceName,
    string SerialNumber,
    string Manufacturer,
    string Model,
    string? PrimaryUserUpn,
    string ComplianceState,
    DateTimeOffset LastSyncDateTime);
