using Giim.Domain.Assets;

namespace Giim.Domain.Reconciliation;

public enum Finding
{
    /// <summary>Enrolled in Intune but not in the register.</summary>
    IntuneOnly,

    /// <summary>An in-use device of an Intune-managed category that Intune doesn't know about.</summary>
    NotInIntune,

    /// <summary>In Intune, but hasn't checked in for longer than the stale threshold.</summary>
    Stale,

    /// <summary>Intune's primary user doesn't look like the person the register says has it.</summary>
    OwnerMismatch,

    /// <summary>Register says returned/spare/lost/disposed, but Intune shows someone still using it.</summary>
    StatusConflict,
}

/// <summary>The register side of the comparison.</summary>
public sealed record RegisterEntry(
    Guid AssetId, string SerialNumber, string? AssetTag, string Category, bool IsIntuneManaged,
    AssetStatus Status, string? Owner);

/// <summary>The Intune side of the comparison.</summary>
public sealed record IntuneEntry(
    string IntuneId, string? SerialNumber, string DeviceName, string? Model, string? UserPrincipalName,
    DateTimeOffset? LastSync);

public sealed record ReconciliationRow(
    string? SerialNumber, RegisterEntry? Register, IntuneEntry? Intune, IReadOnlyList<Finding> Findings)
{
    public bool IsClean => Findings.Count == 0;
}

public sealed record ReconciliationOptions(int StaleAfterDays = 90, int RecentlyActiveDays = 30);

/// <summary>
/// Compares the register with Intune by serial number. Pure logic: no database, no clock, no network,
/// so every rule is unit-tested and the same result comes out every time.
/// </summary>
public static class Reconciler
{
    private static readonly AssetStatus[] NotExpectedInUse =
        [AssetStatus.InStock, AssetStatus.Returned, AssetStatus.Wiped, AssetStatus.Lost, AssetStatus.Disposed];

    public static IReadOnlyList<ReconciliationRow> Reconcile(
        IEnumerable<RegisterEntry> register, IEnumerable<IntuneEntry> intune, DateTimeOffset now, ReconciliationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(intune);
        options ??= new ReconciliationOptions();

        // A re-enrolled device can appear more than once in Intune; the latest check-in is the one that counts.
        var intuneBySerial = intune
            .Where(d => d.SerialNumber is not null)
            .GroupBy(d => d.SerialNumber!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.LastSync ?? DateTimeOffset.MinValue)!, StringComparer.Ordinal);

        var rows = new List<ReconciliationRow>();
        var matchedSerials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in register)
        {
            intuneBySerial.TryGetValue(asset.SerialNumber, out var device);
            var findings = new List<Finding>();

            if (device is null)
            {
                if (asset.IsIntuneManaged && asset.Status == AssetStatus.Assigned)
                    findings.Add(Finding.NotInIntune);
            }
            else
            {
                matchedSerials.Add(asset.SerialNumber);
                var sinceSync = device.LastSync is { } last ? now - last : TimeSpan.MaxValue;

                if (sinceSync > TimeSpan.FromDays(options.StaleAfterDays))
                    findings.Add(Finding.Stale);

                if (NotExpectedInUse.Contains(asset.Status) && device.UserPrincipalName is not null
                    && sinceSync <= TimeSpan.FromDays(options.RecentlyActiveDays))
                    findings.Add(Finding.StatusConflict);

                if (asset.Owner is not null && device.UserPrincipalName is not null
                    && !OwnerMatches(asset.Owner, device.UserPrincipalName))
                    findings.Add(Finding.OwnerMismatch);
            }

            rows.Add(new ReconciliationRow(asset.SerialNumber, asset, device, findings));
        }

        foreach (var device in intune)
        {
            if (device.SerialNumber is not null && matchedSerials.Contains(device.SerialNumber)) continue;
            // Only report the latest record for a duplicated serial, and every serial-less device.
            if (device.SerialNumber is not null && !ReferenceEquals(intuneBySerial[device.SerialNumber], device)) continue;

            rows.Add(new ReconciliationRow(device.SerialNumber, null, device, [Finding.IntuneOnly]));
        }

        return rows;
    }

    /// <summary>
    /// Until people are imported from AD the register only has a free-text name, so compare it with the
    /// name part of the UPN: "grace.brown2@contoso.com" matches "Grace Brown".
    /// </summary>
    public static bool OwnerMatches(string registerOwner, string userPrincipalName)
    {
        ArgumentNullException.ThrowIfNull(registerOwner);
        ArgumentNullException.ThrowIfNull(userPrincipalName);

        if (registerOwner.Contains('@', StringComparison.Ordinal))
            return string.Equals(registerOwner.Trim(), userPrincipalName.Trim(), StringComparison.OrdinalIgnoreCase);

        var local = userPrincipalName.Split('@')[0];
        return NameKey(local) == NameKey(registerOwner);
    }

    private static string NameKey(string value) =>
        string.Concat(value.Where(char.IsLetter)).ToUpperInvariant();
}
