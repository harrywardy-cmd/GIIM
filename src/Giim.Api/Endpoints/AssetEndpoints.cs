using System.Text.Json;
using Giim.Api.Security;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal enum AssetAction { MarkReady, MarkWiped, ReportLost, ReportStolen, Recover, AddNote }

internal sealed record AssetActionRequest(
    AssetAction Action,
    AssetStatus? ExpectedStatus,
    string? TicketNumber,
    string? Note,
    DateTimeOffset? OccurredAt,
    string? Method,
    string? Circumstances,
    string? ReportedBy,
    string? PoliceReference,
    string? WhereFound);

internal static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets");

        group.MapGet("/", async (GiimDbContext db, string? search, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var query = db.Assets.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(a => a.SerialNumber.Contains(search) || (a.AssetTag != null && a.AssetTag.Contains(search)));

            var items = await query
                .OrderBy(a => a.SerialNumber)
                .Skip((Math.Max(page, 1) - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model,
                    Category = a.Category!.Name, a.Status, a.Location, a.LegacyAssignedTo,
                    a.PurchaseDate, a.WarrantyExpiry, a.LastSeenInIntune,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/{id:guid}", async (Guid id, GiimDbContext db, AssetLifecycleService lifecycle, CancellationToken ct) =>
        {
            var asset = await db.Assets.AsNoTracking().Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();

            var timeline = await lifecycle.GetTimelineAsync(id, ct);
            return Results.Ok(new
            {
                asset.Id, asset.AssetTag, asset.SerialNumber, asset.Manufacturer, asset.Model,
                Category = asset.Category?.Name, asset.Category?.IsIntuneManaged, asset.Status,
                asset.Location, asset.PurchaseDate, asset.WarrantyExpiry, asset.Supplier, asset.Cost, asset.Notes,
                asset.LegacyAssignedTo, asset.IntuneDeviceId, asset.LastSeenInIntune, asset.CreatedAt, asset.UpdatedAt,
                NextStatuses = AssetLifecycle.NextStatuses(asset.Status),
                Timeline = timeline.Select(e => new
                {
                    e.Id, e.OccurredAt, e.RecordedAt, e.Type, e.FromStatus, e.ToStatus, e.Actor, e.TicketNumber,
                    e.Summary, e.Note,
                    Details = e.DetailsJson is null ? (JsonElement?)null : JsonDocument.Parse(e.DetailsJson).RootElement,
                }),
            });
        });

        group.MapPost("/{id:guid}/actions", async (Guid id, AssetActionRequest request, AssetLifecycleService lifecycle,
            ICurrentUser user, CancellationToken ct) =>
        {
            var context = new ActionContext(user.Name, request.TicketNumber, request.Note, request.OccurredAt);
            Func<Asset, AssetEvent> action = request.Action switch
            {
                AssetAction.MarkReady => a => a.MarkReady(context),
                AssetAction.MarkWiped => a => a.MarkWiped(context, request.Method ?? ""),
                AssetAction.ReportLost => a => a.ReportLost(context, request.Circumstances ?? "", request.ReportedBy),
                AssetAction.ReportStolen => a => a.ReportStolen(context, request.Circumstances ?? "", request.ReportedBy, request.PoliceReference),
                AssetAction.Recover => a => a.Recover(context, request.WhereFound ?? ""),
                AssetAction.AddNote => a => a.AddNote(context),
                _ => throw new DomainException($"Unknown action {request.Action}."),
            };

            try
            {
                var e = await lifecycle.ApplyAsync(id, request.ExpectedStatus, action, ct);
                return Results.Ok(new { e.Id, e.Type, e.FromStatus, e.ToStatus, e.Summary });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DomainException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AssetChangedException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
    }
}
