using Giim.Domain.Assets;
using Giim.Domain.Assignments;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.People;

public sealed record CandidatePerson(Guid Id, string DisplayName, string? UserPrincipalName, string? Department, string? JobTitle, PersonStatus Status);

public sealed record UnresolvedOwner(
    Guid AssetId, string? AssetTag, string SerialNumber, string Device, string LegacyOwner, string? LegacyDepartment, string? IntuneUser,
    OwnerMatchOutcome Outcome, IReadOnlyList<CandidatePerson> Candidates);

public sealed record LegacyOwnerPreview(int Unlinked, int CanLinkAutomatically, int Ambiguous, int NotFound, IReadOnlyList<UnresolvedOwner> Unresolved);

/// <summary>Links assets from the legacy register to the people who hold them.</summary>
public sealed class LegacyOwnerService(GiimDbContext db)
{
    private const string LegacyNote = "Linked from the legacy register; the original issue date is unknown.";

    public async Task<LegacyOwnerPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        var (assets, matches) = await MatchAsync(cancellationToken);
        var candidateIds = matches.Where(m => m.Outcome != OwnerMatchOutcome.Matched).SelectMany(m => m.CandidateIds).ToHashSet();
        var candidates = await db.People.AsNoTracking()
            .Where(p => candidateIds.Contains(p.Id))
            .Select(p => new CandidatePerson(p.Id, p.DisplayName, p.UserPrincipalName, p.Department!.Name, p.JobTitle, p.Status))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var unresolved = matches
            .Where(m => m.Outcome != OwnerMatchOutcome.Matched)
            .Select(m =>
            {
                var a = assets[m.AssetId];
                return new UnresolvedOwner(a.Id, a.AssetTag, a.SerialNumber, $"{a.Manufacturer} {a.Model}", a.LegacyOwner, a.LegacyDepartment, a.IntuneUser,
                    m.Outcome, [.. m.CandidateIds.Select(id => candidates[id])]);
            })
            .OrderBy(u => u.Outcome).ThenBy(u => u.LegacyOwner, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.SerialNumber, StringComparer.Ordinal)
            .ToList();

        return new LegacyOwnerPreview(
            matches.Count,
            matches.Count(m => m.Outcome == OwnerMatchOutcome.Matched),
            matches.Count(m => m.Outcome == OwnerMatchOutcome.Ambiguous),
            matches.Count(m => m.Outcome == OwnerMatchOutcome.NotFound),
            unresolved);
    }

    /// <summary>Links every asset whose owner is certain. Ambiguous and unknown names are left for a technician.</summary>
    public async Task<int> LinkAutomaticAsync(string actor, CancellationToken cancellationToken)
    {
        var (_, matches) = await MatchAsync(cancellationToken);
        var certain = matches.Where(m => m.Outcome == OwnerMatchOutcome.Matched).ToList();
        var context = new ActionContext(actor);
        var linked = 0;

        foreach (var batch in certain.Chunk(500))
        {
            var ids = batch.Select(m => m.AssetId).ToList();
            var assets = await db.Assets.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
            var personIds = batch.Select(m => m.PersonId!.Value).ToHashSet();
            var names = await db.People.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);

            foreach (var match in batch)
            {
                var asset = assets[match.AssetId];
                if (asset.AssignedToPersonId is not null) continue;  // linked by someone else meanwhile
                Link(asset, match.PersonId!.Value, names[match.PersonId.Value], match.MatchedBy!, context);
                linked++;
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        return linked;
    }

    /// <summary>A technician picks the owner for one asset the matcher couldn't resolve.</summary>
    public async Task ResolveAsync(Guid assetId, Guid personId, string actor, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
            ?? throw new KeyNotFoundException("Person not found.");

        Link(asset, person.Id, person.DisplayName, "technician's choice", new ActionContext(actor));
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Link(Asset asset, Guid personId, string personName, string matchedBy, ActionContext context)
    {
        db.AssetEvents.Add(asset.LinkLegacyOwner(context, personId, personName, matchedBy));
        var assignment = Assignment.ForAsset(personId, asset.Id);
        assignment.AssignedBy = context.Actor;
        assignment.Notes = LegacyNote;
        db.Assignments.Add(assignment);
    }

    private sealed record UnlinkedAsset(Guid Id, string? AssetTag, string SerialNumber, string Manufacturer, string Model, string LegacyOwner, string? LegacyDepartment, string? IntuneUser);

    private async Task<(Dictionary<Guid, UnlinkedAsset> Assets, IReadOnlyList<OwnerMatch> Matches)> MatchAsync(CancellationToken cancellationToken)
    {
        var intuneUsers = await db.ManagedDevices.AsNoTracking()
            .Where(d => d.RemovedFromIntuneAt == null && d.SerialNumber != null && d.UserPrincipalName != null)
            .Select(d => new { d.SerialNumber, d.UserPrincipalName, d.LastSyncDateTime })
            .ToListAsync(cancellationToken);
        var intuneBySerial = intuneUsers
            .GroupBy(d => d.SerialNumber!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.LastSyncDateTime)!.UserPrincipalName, StringComparer.Ordinal);

        var assets = (await db.Assets.AsNoTracking()
                .Where(a => (a.Status == AssetStatus.Assigned || a.Status == AssetStatus.ReturnRequested)
                    && a.AssignedToPersonId == null && a.LegacyAssignedTo != null)
                .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, a.LegacyAssignedTo, a.LegacyDepartment })
                .ToListAsync(cancellationToken))
            .Select(a => new UnlinkedAsset(a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, a.LegacyAssignedTo!, a.LegacyDepartment,
                intuneBySerial.GetValueOrDefault(a.SerialNumber)))
            .ToDictionary(a => a.Id);

        var people = await db.People.AsNoTracking()
            .Select(p => new OwnerCandidate(p.Id, p.DisplayName, p.UserPrincipalName, p.Department!.Code, p.Department.Name))
            .ToListAsync(cancellationToken);

        var matches = LegacyOwnerMatcher.Match(
            assets.Values.Select(a => new LegacyOwnedAsset(a.Id, a.LegacyOwner, a.IntuneUser, a.LegacyDepartment)), people);
        return (assets, matches);
    }
}
