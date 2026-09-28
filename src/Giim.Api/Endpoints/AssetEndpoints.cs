using System.Text.Json;
using Giim.Api.Security;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal enum AssetAction { MarkReady, MarkWiped, ReportLost, ReportStolen, Recover, RequestReturn, AddNote }

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
    string? WhereFound,
    DateOnly? DueDate);

internal sealed record AssignRequest(
    Guid PersonId,
    AssetStatus? ExpectedStatus,
    string? TicketNumber,
    string? Note,
    string? Location,
    IReadOnlyList<AccessoryRequest>? Accessories);

internal sealed record ReturnRequest(
    AssetStatus? ExpectedStatus,
    string? TicketNumber,
    string? Note,
    AssetCondition Condition,
    string? ReturnedBy,
    IReadOnlyList<Guid>? ReturnedAccessoryIds,
    string? ReturnStockTo);

internal static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets");

        group.MapGet("/", async (GiimDbContext db, string? search, AssetStatus? status, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var query = db.Assets.AsNoTracking();

            if (status is { } s)
                query = query.Where(a => a.Status == s);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(a => a.SerialNumber.Contains(search) || (a.AssetTag != null && a.AssetTag.Contains(search))
                    || a.Model.Contains(search));

            var items = await query
                .OrderBy(a => a.SerialNumber)
                .Skip((Math.Max(page, 1) - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model,
                    Category = a.Category!.Name, a.Status, a.Location, a.LegacyAssignedTo, a.AssignedToPersonId,
                    AssignedTo = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.DisplayName).FirstOrDefault(),
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
            var holder = asset.AssignedToPersonId is { } holderId
                ? await db.People.AsNoTracking().Where(p => p.Id == holderId)
                    .Select(p => new { p.Id, p.DisplayName, p.UserPrincipalName, Department = p.Department!.Name, p.Status }).FirstOrDefaultAsync(ct)
                : null;
            var assignment = await db.Assignments.AsNoTracking().Include(a => a.Accessories)
                .FirstOrDefaultAsync(a => a.AssetId == id && a.EndedAt == null, ct);

            return Results.Ok(new
            {
                asset.Id, asset.AssetTag, asset.SerialNumber, asset.Manufacturer, asset.Model,
                Category = asset.Category?.Name, asset.Category?.IsIntuneManaged, asset.Status,
                asset.Location, asset.PurchaseDate, asset.WarrantyExpiry, asset.Supplier, asset.Cost, asset.Notes,
                asset.LegacyAssignedTo, AssignedTo = holder, asset.IntuneDeviceId, asset.LastSeenInIntune, asset.CreatedAt, asset.UpdatedAt,
                NextStatuses = AssetLifecycle.NextStatuses(asset.Status),
                CurrentAssignment = assignment is null ? null : new
                {
                    assignment.Id, assignment.AssignedAt, assignment.AssignedBy, TicketNumber = assignment.ServiceDeskRequestId,
                    assignment.Notes,
                    Accessories = assignment.Accessories.Select(x => new
                    {
                        x.Id, x.Description, x.Quantity, x.Label, x.AccessoryAssetId, x.StockItemId, x.Status,
                    }),
                },
                Timeline = timeline.Select(e => new
                {
                    e.Id, e.OccurredAt, e.RecordedAt, e.Type, e.FromStatus, e.ToStatus, e.Actor, e.TicketNumber,
                    e.Summary, e.Note,
                    Details = e.DetailsJson is null ? (JsonElement?)null : JsonDocument.Parse(e.DetailsJson).RootElement,
                }),
            });
        });

        group.MapPost("/{id:guid}/actions", (Guid id, AssetActionRequest request, AssetLifecycleService lifecycle,
            ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var context = new ActionContext(user.Name, request.TicketNumber, request.Note, request.OccurredAt);
            Func<Asset, AssetEvent> action = request.Action switch
            {
                AssetAction.MarkReady => a => a.MarkReady(context),
                AssetAction.MarkWiped => a => a.MarkWiped(context, request.Method ?? ""),
                AssetAction.ReportLost => a => a.ReportLost(context, request.Circumstances ?? "", request.ReportedBy),
                AssetAction.ReportStolen => a => a.ReportStolen(context, request.Circumstances ?? "", request.ReportedBy, request.PoliceReference),
                AssetAction.Recover => a => a.Recover(context, request.WhereFound ?? ""),
                AssetAction.RequestReturn => a => a.RequestReturn(context, request.DueDate),
                AssetAction.AddNote => a => a.AddNote(context),
                _ => throw new DomainException($"Unknown action {request.Action}."),
            };

            var e = await lifecycle.ApplyAsync(id, request.ExpectedStatus, action, ct);
            return Results.Ok(new { e.Id, e.Type, e.FromStatus, e.ToStatus, e.Summary });
        }));

        group.MapPost("/{id:guid}/assign", (Guid id, AssignRequest request, AssignmentService assignments,
            ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var context = new ActionContext(user.Name, request.TicketNumber, request.Note);
            var assignment = await assignments.AssignAsync(id, request.PersonId, request.ExpectedStatus, context,
                request.Location, request.Accessories ?? [], ct);
            return Results.Ok(new { assignment.Id, Accessories = assignment.Accessories.Select(a => a.Label) });
        }));

        group.MapPost("/{id:guid}/return", (Guid id, ReturnRequest request, AssignmentService assignments,
            ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var context = new ActionContext(user.Name, request.TicketNumber, request.Note);
            var missing = await assignments.ReturnAsync(id, request.ExpectedStatus, context, request.Condition,
                request.ReturnedBy, (request.ReturnedAccessoryIds ?? []).ToHashSet(), request.ReturnStockTo, ct);
            return Results.Ok(new { Missing = missing.Select(m => m.Label) });
        }));
    }

    /// <summary>Maps domain outcomes to HTTP: rule broken 400, not found 404, changed by someone else 409.</summary>
    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (DomainException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (AssetChangedException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
