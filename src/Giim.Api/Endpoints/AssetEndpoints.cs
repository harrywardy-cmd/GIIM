using System.Text.Json;
using Giim.Api.Security;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Importing;
using Giim.Domain.Repairs;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal enum AssetAction { MarkReady, MarkWiped, ReportLost, ReportStolen, Recover, RequestReturn, Retire, Dispose, AddNote }

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
    DateOnly? DueDate,
    string? Reason,
    DataSanitisation? DataSanitisation,
    string? FinalLocation,
    DisposalMethod? DisposalMethod,
    string? DisposalCompany,
    string? CertificateNumber,
    DateOnly? DisposedOn);

internal sealed record OpenRepairRequest(
    AssetStatus? ExpectedStatus, string Fault, string? Vendor, bool WarrantyClaim, string? VendorReference, DateOnly? SentOn,
    string? TicketNumber, string? Note);

internal sealed record CompleteRepairRequest(
    RepairOutcome Outcome, string? Diagnosis, string? WorkPerformed, decimal? Cost, string? TicketNumber, string? Note);

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

internal sealed record CreateAssetRequest(
    string SerialNumber,
    string Manufacturer,
    string Model,
    Guid CategoryId,
    string? AssetTag,
    string? Location,
    DateOnly? PurchaseDate,
    DateOnly? WarrantyExpiry,
    string? Supplier,
    decimal? Cost,
    string? PurchaseOrder,
    string? Notes,
    AssetStatus? StartAs,
    string? TicketNumber);

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
            {
                var term = search.Trim();
                // Serials are stored without spaces or dashes, so search the same way: "5cg 123-a" finds 5CG123A.
                var serialTerm = ImportNormalizer.Serial(term) ?? term;
                query = query.Where(a => a.SerialNumber.Contains(serialTerm) || (a.AssetTag != null && a.AssetTag.Contains(term))
                    || a.Model.Contains(term)
                    || db.People.Any(p => p.Id == a.AssignedToPersonId && p.DisplayName.Contains(term)));
            }

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

        group.MapGet("/lookup", async (string code, AssetLifecycleService lifecycle, CancellationToken ct) =>
            await lifecycle.LookupAsync(code, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound());

        group.MapPost("/", async (CreateAssetRequest request, AssetLifecycleService lifecycle, ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                var details = new NewAsset(request.SerialNumber ?? "", request.Manufacturer ?? "", request.Model ?? "", request.CategoryId,
                    request.AssetTag, request.Location, request.PurchaseDate, request.WarrantyExpiry, request.Supplier, request.Cost,
                    request.PurchaseOrder, request.Notes);
                var asset = await lifecycle.CreateAsync(details, new ActionContext(user.Name, request.TicketNumber),
                    request.StartAs ?? AssetStatus.Received, ct);
                return Results.Created($"/api/assets/{asset.Id}", new { asset.Id, asset.SerialNumber, asset.Status });
            }
            catch (DuplicateAssetException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["existingAssetId"] = e.ExistingAssetId });
            }
            catch (DomainException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, GiimDbContext db, AssetLifecycleService lifecycle, RepairService repairs, CancellationToken ct) =>
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
            var owners = await (
                    from s in db.Assignments.AsNoTracking()
                    join p in db.People.AsNoTracking() on s.PersonId equals p.Id
                    where s.AssetId == id
                    orderby s.AssignedAt descending
                    select new
                    {
                        s.Id, PersonId = p.Id, p.DisplayName, s.AssignedAt, s.EndedAt, s.AssignedBy, s.ReceivedBy,
                        TicketNumber = s.ServiceDeskRequestId, s.ReturnTicketNumber, s.ReturnCondition, s.Notes,
                        Missing = s.Accessories.Where(x => x.Status == Domain.Assignments.AccessoryStatus.Missing).Select(x => x.Description).ToList(),
                    })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                asset.Id, asset.AssetTag, asset.SerialNumber, asset.Manufacturer, asset.Model,
                Category = asset.Category?.Name, asset.Category?.IsIntuneManaged, asset.Status,
                asset.Location, asset.PurchaseDate, asset.WarrantyExpiry, asset.Supplier, asset.Cost, asset.Notes,
                asset.LegacyAssignedTo, AssignedTo = holder, asset.IntuneDeviceId, asset.LastSeenInIntune, asset.CreatedAt, asset.UpdatedAt,
                NextStatuses = AssetLifecycle.NextStatuses(asset.Status),
                Owners = owners,
                Repairs = (await repairs.ForAssetAsync(id, ct)).Select(r => new
                {
                    r.Id, r.Fault, r.Vendor, r.WarrantyClaim, r.VendorReference, r.SentOn, r.OpenedBy, r.OpenedAt, r.TicketNumber,
                    r.Diagnosis, r.WorkPerformed, r.Cost, r.Outcome, r.CompletedBy, r.CompletedAt, r.IsOpen,
                    DurationDays = r.Duration is { } d ? Math.Round(d.TotalDays, 1) : (double?)null,
                }),
                EndOfLife = asset.RetiredAt is null ? null : new
                {
                    asset.RetiredAt, asset.RetirementReason, asset.DataSanitisation,
                    asset.DisposedOn, asset.DisposalMethod, asset.DisposalCompany, asset.DisposalCertificate,
                },
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
                AssetAction.Retire => a => a.Retire(context, request.Reason ?? "",
                    request.DataSanitisation ?? throw new DomainException("Record how the data was dealt with."), request.FinalLocation),
                AssetAction.Dispose => a => a.RecordDisposal(context,
                    request.DisposalMethod ?? throw new DomainException("Choose a disposal method."),
                    request.DisposalCompany, request.CertificateNumber,
                    request.DisposedOn ?? DateOnly.FromDateTime(DateTime.UtcNow)),
                AssetAction.AddNote => a => a.AddNote(context),
                _ => throw new DomainException($"Unknown action {request.Action}."),
            };

            var e = await lifecycle.ApplyAsync(id, request.ExpectedStatus, action, ct);
            return Results.Ok(new { e.Id, e.Type, e.FromStatus, e.ToStatus, e.Summary });
        }));

        group.MapPost("/{id:guid}/repairs", (Guid id, OpenRepairRequest request, RepairService repairs,
            ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var repair = await repairs.OpenAsync(id, request.ExpectedStatus, new ActionContext(user.Name, request.TicketNumber, request.Note),
                request.Fault ?? "", request.Vendor, request.WarrantyClaim, request.VendorReference, request.SentOn, ct);
            return Results.Ok(new { repair.Id });
        }));

        group.MapPost("/{id:guid}/repairs/{repairId:guid}/complete", (Guid id, Guid repairId, CompleteRepairRequest request,
            RepairService repairs, ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var repair = await repairs.CompleteAsync(id, repairId, new ActionContext(user.Name, request.TicketNumber, request.Note),
                request.Outcome, request.Diagnosis, request.WorkPerformed, request.Cost, ct);
            return Results.Ok(new { repair.Id, repair.Outcome });
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
