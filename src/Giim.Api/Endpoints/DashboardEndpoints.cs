using Giim.Domain.Assets;
using Giim.Domain.People;
using Giim.Infrastructure.Devices;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Reports;
using Giim.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (GiimDbContext db, StockService stock, DashboardHistory history, TimeProvider clock,
            CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var inService = db.Assets.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Retired);

            var byStatus = await db.Assets.GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
            int Count(params AssetStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));

            var warrantyHorizon = today.AddDays(90);
            var warrantyExpiring = await inService
                .Where(a => a.WarrantyExpiry != null && a.WarrantyExpiry >= today && a.WarrantyExpiry <= warrantyHorizon)
                .OrderBy(a => a.WarrantyExpiry)
                .Take(6)
                .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name, a.WarrantyExpiry })
                .ToListAsync(ct);

            var recent = await (
                    from e in db.AssetEvents.AsNoTracking()
                    join a in db.Assets.AsNoTracking() on e.AssetId equals a.Id
                    orderby e.OccurredAt descending, e.Id descending
                    select new { e.Id, e.OccurredAt, e.Type, e.Summary, e.Actor, e.TicketNumber, AssetId = a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model })
                .Take(10)
                .ToListAsync(ct);

            var staleSince = now.AddDays(-90);
            var levels = await stock.GetLevelsAsync(ct);
            var lastIntuneSync = await db.SyncRuns.AsNoTracking()
                .Where(r => r.Source == IntuneSyncService.Source && r.Status == Domain.Devices.SyncRunStatus.Succeeded)
                .OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.CompletedAt).FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                Totals = DashboardTotals.From(byStatus, await db.DeviceRequests.CountAsync(r =>
                    r.Status == Domain.Requests.RequestStatus.PendingApproval || r.Status == Domain.Requests.RequestStatus.InfoRequested, ct)),
                // The same totals on an earlier day (about a week ago), for the arrows on the cards.
                Trend = await history.TrendAsync(ct),
                // Part-to-whole of assets in service, in a fixed category order so colours never move.
                StatusBreakdown = new[]
                {
                    new { Key = "Assigned", Label = "Assigned", Count = Count(AssetStatus.Assigned, AssetStatus.ReturnRequested) },
                    new { Key = "Available", Label = "Ready to deploy", Count = Count(AssetStatus.ReadyToDeploy) },
                    new { Key = "Processing", Label = "Received / returned / wiped", Count = Count(AssetStatus.Received, AssetStatus.Returned, AssetStatus.Wiped) },
                    new { Key = "InRepair", Label = "In repair", Count = Count(AssetStatus.InRepair) },
                    new { Key = "Missing", Label = "Lost / stolen", Count = Count(AssetStatus.Lost, AssetStatus.Stolen) },
                },
                WarrantyExpiring = warrantyExpiring.Select(w => new
                {
                    w.Id, w.AssetTag, w.SerialNumber, w.Manufacturer, w.Model, w.Category, w.WarrantyExpiry,
                    DaysLeft = w.WarrantyExpiry!.Value.DayNumber - today.DayNumber,
                }),
                Attention = new
                {
                    LeaversWithKit = await db.People.CountAsync(p => p.Status == PersonStatus.Left && db.Assets.Any(a => a.AssignedToPersonId == p.Id), ct),
                    ReturnsRequested = Count(AssetStatus.ReturnRequested),
                    AwaitingWipe = Count(AssetStatus.Returned),
                    OpenRepairs = await db.Repairs.CountAsync(r => r.CompletedAt == null, ct),
                    UnlinkedOwners = await db.Assets.CountAsync(a => a.Status == AssetStatus.Assigned && a.AssignedToPersonId == null && a.LegacyAssignedTo != null, ct),
                    NotSeenInIntune = await inService.CountAsync(a => a.Category!.IsIntuneManaged && a.Status == AssetStatus.Assigned
                        && a.LastSeenInIntune != null && a.LastSeenInIntune < staleSince, ct),
                    WarrantyExpired = await inService.CountAsync(a => a.WarrantyExpiry != null && a.WarrantyExpiry < today, ct),
                    LowStock = levels.Count(l => l.IsLow),
                },
                LastIntuneSync = lastIntuneSync,
                RecentActivity = recent,
            });
        });
    }
}
