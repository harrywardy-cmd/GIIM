namespace Giim.Domain.People;

public enum OwnerMatchOutcome
{
    /// <summary>Exactly one person fits; safe to link automatically.</summary>
    Matched,

    /// <summary>Several people have this name and nothing else tells them apart; a technician must choose.</summary>
    Ambiguous,

    /// <summary>Nobody in the directory has this name.</summary>
    NotFound,
}

public sealed record OwnerCandidate(
    Guid PersonId, string DisplayName, string? UserPrincipalName, string? DepartmentCode = null, string? DepartmentName = null);

/// <summary>An asset whose owner is only known as free text from the legacy register.</summary>
public sealed record LegacyOwnedAsset(
    Guid AssetId, string LegacyOwner, string? IntuneUserPrincipalName, string? LegacyDepartment = null);

public sealed record OwnerMatch(
    Guid AssetId, OwnerMatchOutcome Outcome, Guid? PersonId, string? MatchedBy, IReadOnlyList<Guid> CandidateIds);

/// <summary>
/// Turns "Assigned To" names from the old spreadsheet into real people. It never guesses: shared names are only
/// resolved when Intune's primary user identifies one of them; otherwise a technician chooses.
/// </summary>
public static class LegacyOwnerMatcher
{
    public static IReadOnlyList<OwnerMatch> Match(IEnumerable<LegacyOwnedAsset> assets, IEnumerable<OwnerCandidate> people)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(people);

        var directory = people.ToList();
        var byName = directory.ToLookup(p => NameKey(p.DisplayName));
        var byUpn = directory.Where(p => p.UserPrincipalName is not null)
            .ToDictionary(p => p.UserPrincipalName!, StringComparer.OrdinalIgnoreCase);

        return assets.Select(asset =>
        {
            // Some registers record the email address rather than the name.
            if (asset.LegacyOwner.Contains('@', StringComparison.Ordinal))
            {
                return byUpn.TryGetValue(asset.LegacyOwner.Trim(), out var byEmail)
                    ? new OwnerMatch(asset.AssetId, OwnerMatchOutcome.Matched, byEmail.PersonId, "email address", [byEmail.PersonId])
                    : new OwnerMatch(asset.AssetId, OwnerMatchOutcome.NotFound, null, null, []);
            }

            var candidates = byName[NameKey(asset.LegacyOwner)].ToList();
            switch (candidates.Count)
            {
                case 1:
                    return new OwnerMatch(asset.AssetId, OwnerMatchOutcome.Matched, candidates[0].PersonId, "name", [candidates[0].PersonId]);

                case > 1:
                    var ids = candidates.Select(c => c.PersonId).ToList();

                    // Intune's primary user is the strongest evidence of who really has the device.
                    var intuneUser = candidates.FirstOrDefault(c => c.UserPrincipalName is not null
                        && string.Equals(c.UserPrincipalName, asset.IntuneUserPrincipalName, StringComparison.OrdinalIgnoreCase));
                    if (intuneUser is not null)
                        return new OwnerMatch(asset.AssetId, OwnerMatchOutcome.Matched, intuneUser.PersonId, "name and Intune user", ids);

                    // Then the department written next to the name in the register (code or name).
                    if (asset.LegacyDepartment is { } department)
                    {
                        var key = DepartmentKey(department);
                        var inDepartment = candidates.Where(c =>
                            (c.DepartmentCode is not null && DepartmentKey(c.DepartmentCode) == key)
                            || (c.DepartmentName is not null && DepartmentKey(c.DepartmentName) == key)).ToList();
                        if (inDepartment.Count == 1)
                            return new OwnerMatch(asset.AssetId, OwnerMatchOutcome.Matched, inDepartment[0].PersonId, "name and department", ids);
                    }

                    return new OwnerMatch(asset.AssetId, OwnerMatchOutcome.Ambiguous, null, null, ids);

                default:
                    // Offer Intune's user as a suggestion, but don't link: the name says someone else.
                    var suggestion = asset.IntuneUserPrincipalName is not null && byUpn.TryGetValue(asset.IntuneUserPrincipalName, out var s)
                        ? new[] { s.PersonId } : [];
                    return new OwnerMatch(asset.AssetId, OwnerMatchOutcome.NotFound, null, null, suggestion);
            }
        }).ToList();
    }

    private static string DepartmentKey(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>"Brown, Grace" and "grace  brown" are the same name; so are "O'Brien" and "OBrien".</summary>
    public static string NameKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var parts = name.Contains(',', StringComparison.Ordinal)
            ? name.Split(',', 2).Reverse()
            : [name];
        return string.Concat(string.Concat(parts).Where(char.IsLetter)).ToUpperInvariant();
    }
}
