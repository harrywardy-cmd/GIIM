using Giim.Domain.Reconciliation;
using Giim.Infrastructure.Devices;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal static class IntuneEndpoints
{
    public static void MapIntuneEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/intune/sync", async (IntuneSyncService sync, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await sync.SyncAsync(ct));
            }
            catch (InvalidOperationException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (Exception e) when (e is HttpRequestException or FileNotFoundException or InvalidDataException)
            {
                return Results.Problem($"Intune sync failed: {e.Message}", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/intune/sync-runs", async (GiimDbContext db, CancellationToken ct) =>
            Results.Ok(await db.SyncRuns.AsNoTracking()
                .Where(r => r.Source == IntuneSyncService.Source)
                .OrderByDescending(r => r.StartedAt)
                .Take(20)
                .ToListAsync(ct)));

        app.MapGet("/api/reconciliation", async (ReconciliationService reconciliation, Finding? finding, string? search,
            CancellationToken ct, int page = 1, int pageSize = 100) =>
        {
            var report = await reconciliation.GetReportAsync(finding, search, page, pageSize, ct);
            return Results.Ok(new
            {
                report.GeneratedAt,
                report.LastSync,
                report.RegisterAssets,
                report.IntuneDevices,
                report.Clean,
                report.FindingCounts,
                report.FilteredCount,
                Rows = report.Rows.Select(r => new
                {
                    r.SerialNumber,
                    r.Findings,
                    AssetId = r.Register?.AssetId,
                    AssetTag = r.Register?.AssetTag,
                    Category = r.Register?.Category,
                    Status = r.Register?.Status,
                    RegisterOwner = r.Register?.Owner,
                    IntuneDeviceName = r.Intune?.DeviceName,
                    IntuneModel = r.Intune?.Model,
                    IntuneUser = r.Intune?.UserPrincipalName,
                    IntuneLastSync = r.Intune?.LastSync,
                }),
            });
        });
    }
}
