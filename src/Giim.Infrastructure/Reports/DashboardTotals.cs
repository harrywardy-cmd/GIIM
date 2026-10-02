using Giim.Domain.Assets;
using Giim.Domain.Reporting;
using Giim.Domain.Requests;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Reports;

/// <summary>The dashboard's headline numbers. Worked out in one place, so today's and a past day's always compare.</summary>
public sealed record DashboardTotals(int InService, int Assigned, int Available, int InRepair, int Retired, int Disposed, int PendingApproval)
{
    public static DashboardTotals From(IReadOnlyDictionary<AssetStatus, int> byStatus, int pendingApproval)
    {
        ArgumentNullException.ThrowIfNull(byStatus);
        int Count(params AssetStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        return new DashboardTotals(
            InService: Count(AssetStatus.Received, AssetStatus.ReadyToDeploy, AssetStatus.Assigned, AssetStatus.ReturnRequested,
                AssetStatus.Returned, AssetStatus.Wiped, AssetStatus.InRepair, AssetStatus.Lost, AssetStatus.Stolen),
            Assigned: Count(AssetStatus.Assigned, AssetStatus.ReturnRequested),
            Available: Count(AssetStatus.ReadyToDeploy),
            InRepair: Count(AssetStatus.InRepair),
            Retired: Count(AssetStatus.Retired),
            Disposed: Count(AssetStatus.Disposed),
            PendingApproval: pendingApproval);
    }

    public static DashboardTotals From(DashboardSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.InService, s.Assigned, s.Available, s.InRepair, s.Retired, s.Disposed, s.PendingApproval);
    }
}

/// <summary>The totals on an earlier day, to compare with now.</summary>
public sealed record DashboardTrend(DateOnly Since, DashboardTotals Then);

/// <summary>
/// Daily snapshots of the dashboard totals (taken by the workers through the day, so each day keeps its last count) and
/// the comparison the dashboard shows: with a week ago, or the oldest day there is until a week has built up.
/// </summary>
public sealed class DashboardHistory(GiimDbContext db, IOptions<ReminderOptions> reminders, TimeProvider clock)
{
    public const int CompareDays = 7;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById(reminders.Value.TimeZone)).DateTime);

    public async Task<DashboardTotals> CurrentAsync(CancellationToken cancellationToken)
    {
        var byStatus = await db.Assets.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var pending = await db.DeviceRequests.CountAsync(r =>
            r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.InfoRequested, cancellationToken);
        return DashboardTotals.From(byStatus, pending);
    }

    /// <summary>Records today's totals (replacing an earlier snapshot today).</summary>
    public async Task TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var totals = await CurrentAsync(cancellationToken);
        var snapshot = await db.DashboardSnapshots.FirstOrDefaultAsync(s => s.Date == today, cancellationToken);
        if (snapshot is null) db.DashboardSnapshots.Add(snapshot = new DashboardSnapshot { Date = today });
        snapshot.TakenAt = clock.GetUtcNow();
        (snapshot.InService, snapshot.Assigned, snapshot.Available, snapshot.InRepair, snapshot.Retired, snapshot.Disposed, snapshot.PendingApproval) =
            (totals.InService, totals.Assigned, totals.Available, totals.InRepair, totals.Retired, totals.Disposed, totals.PendingApproval);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The day to compare with: the latest at least a week ago, else the oldest before today; null with no history.</summary>
    public async Task<DashboardTrend?> TrendAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var weekAgo = today.AddDays(-CompareDays);
        var then = await db.DashboardSnapshots.AsNoTracking().Where(s => s.Date <= weekAgo).OrderByDescending(s => s.Date)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await db.DashboardSnapshots.AsNoTracking().Where(s => s.Date < today).OrderBy(s => s.Date).FirstOrDefaultAsync(cancellationToken);
        return then is null ? null : new DashboardTrend(then.Date, DashboardTotals.From(then));
    }
}
