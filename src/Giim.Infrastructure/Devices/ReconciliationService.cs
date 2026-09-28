using Giim.Domain.Devices;
using Giim.Domain.Reconciliation;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Devices;

public sealed record ReconciliationReport(
    DateTimeOffset GeneratedAt,
    SyncRun? LastSync,
    int RegisterAssets,
    int IntuneDevices,
    int Clean,
    IReadOnlyDictionary<Finding, int> FindingCounts,
    int FilteredCount,
    IReadOnlyList<ReconciliationRow> Rows);

/// <summary>Runs the reconciliation rules over the register and the latest Intune snapshot.</summary>
public sealed class ReconciliationService(GiimDbContext db, TimeProvider clock)
{
    public async Task<ReconciliationReport> GetReportAsync(
        Finding? finding, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var register = await db.Assets.AsNoTracking()
            .Where(a => a.Status != Domain.Assets.AssetStatus.Disposed)
            .Select(a => new RegisterEntry(a.Id, a.SerialNumber, a.AssetTag, a.Category!.Name, a.Category.IsIntuneManaged,
                a.Status, a.LegacyAssignedTo))
            .ToListAsync(cancellationToken);

        var intune = await db.ManagedDevices.AsNoTracking()
            .Where(d => d.RemovedFromIntuneAt == null)
            .Select(d => new IntuneEntry(d.IntuneId, d.SerialNumber, d.DeviceName, d.Model, d.UserPrincipalName, d.LastSyncDateTime))
            .ToListAsync(cancellationToken);

        var lastSync = await db.SyncRuns.AsNoTracking()
            .Where(r => r.Source == IntuneSyncService.Source)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var rows = Reconciler.Reconcile(register, intune, now);

        IEnumerable<ReconciliationRow> filtered = finding is { } f
            ? rows.Where(r => r.Findings.Contains(f))
            : rows.Where(r => !r.IsClean);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(r =>
                Contains(r.SerialNumber, term) || Contains(r.Register?.AssetTag, term) || Contains(r.Register?.Owner, term)
                || Contains(r.Intune?.UserPrincipalName, term) || Contains(r.Intune?.DeviceName, term));
        }

        var matching = filtered.OrderBy(r => r.SerialNumber, StringComparer.Ordinal).ToList();
        pageSize = Math.Clamp(pageSize, 1, 500);

        return new ReconciliationReport(
            now,
            lastSync,
            register.Count,
            intune.Count,
            rows.Count(r => r.IsClean),
            rows.SelectMany(r => r.Findings).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()),
            matching.Count,
            matching.Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToList());
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
