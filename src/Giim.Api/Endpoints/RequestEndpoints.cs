using Giim.Api.Security;
using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Requests;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal enum Decision { Approve, Reject, AskForInfo }

internal sealed record DecisionRequest(Decision Decision, string? Comment, string? BudgetCode, RequestStatus? ExpectedStatus);
internal sealed record TextRequest(string? Text, RequestStatus? ExpectedStatus);
internal sealed record OrderRequest(string? Supplier, string? PurchaseOrder, decimal? Cost, DateOnly? OrderedOn, DateOnly? ExpectedDelivery,
    string? TrackingNumber, string? Notes, RequestStatus? ExpectedStatus);
internal sealed record ReceiveRequest(string? SerialNumber, string? AssetTag, string? Manufacturer, string? Model, Guid? CategoryId,
    DateOnly? WarrantyExpiry, DateOnly? PurchaseDate, decimal? Cost, string? Notes, Guid? LocationId, bool ReadyToIssue,
    RequestStatus? ExpectedStatus);
internal sealed record FulfilRequest(Guid AssetId, AssetStatus? ExpectedAssetStatus, Guid? LocationId, string? TicketNumber,
    IReadOnlyList<AccessoryRequest>? Accessories, RequestStatus? ExpectedStatus);

/// <summary>
/// Device requests and approvals. Managers can raise, decide, answer, cancel and comment (the request checks they
/// are the named approver before a decision); ordering, receiving and handover are for IT.
/// </summary>
internal static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requests");

        group.MapGet("", async (RequestService requests, ICurrentUser user, RequestView? view, string? search, Guid? personId,
            CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var actor = await ActorAsync(requests, user, ct);
            var list = await requests.ListAsync(view ?? RequestView.All, search, personId, actor, page, Math.Clamp(pageSize, 1, 200), ct);
            return Results.Ok(list);
        });

        group.MapGet("/awaiting-count", async (RequestService requests, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(new { count = await requests.AwaitingCountAsync(await ActorAsync(requests, user, ct), ct) }));

        group.MapGet("/{id:guid}", async (Guid id, RequestService requests, GiimDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            if (await requests.GetAsync(id, ct) is not { } found) return Results.NotFound();
            var (r, history, emails) = found;
            var actor = await ActorAsync(requests, user, ct);

            var recipient = await db.People.AsNoTracking().Where(p => p.Id == r.RecipientPersonId)
                .Select(p => new
                {
                    p.Id, p.DisplayName, p.JobTitle, p.Status, Email = p.Email ?? p.UserPrincipalName, p.StartDate,
                    Manager = db.People.Where(m => m.Id == p.ManagerId).Select(m => m.DisplayName).FirstOrDefault(),
                })
                .FirstAsync(ct);
            var approver = await db.People.AsNoTracking().Where(p => p.Id == r.ApproverPersonId)
                .Select(p => new { p.Id, p.DisplayName, Email = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(ct);
            var asset = r.AssetId is { } assetId
                ? await db.Assets.AsNoTracking().Where(a => a.Id == assetId)
                    .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, a.Status }).FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(new
            {
                r.Id, r.Number, Reference = r.Reference, r.Status, r.Priority, r.ReasonType, r.Reason, r.DeviceDescription, r.Specifications,
                r.Notes, r.NeededBy, r.EstimatedCost, r.BudgetCode, r.TicketNumber, r.SubmittedAt,
                r.RequestedBy, r.RequestedByName, r.RequestedByEmail,
                Category = await db.AssetCategories.Where(c => c.Id == r.CategoryId).Select(c => new { c.Id, c.Name }).FirstAsync(ct),
                Department = await db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct),
                Recipient = recipient,
                Approver = approver,
                Decision = r.DecidedAt is null ? null : new { r.DecidedByName, r.DecidedAt, r.DecisionComment },
                Order = r.OrderedOn is null ? null : new { r.Supplier, r.PurchaseOrder, r.OrderCost, r.OrderedOn, r.ExpectedDelivery, r.TrackingNumber, r.PurchaseNotes },
                Asset = asset,
                r.ReceivedAt, r.CompletedAt, r.CancellationReason,
                History = history.Select(e => new { e.Id, e.OccurredAt, e.Type, e.FromStatus, e.ToStatus, e.Actor, e.ActorName, e.Summary, e.Comment }),
                Emails = emails.Select(n => new { n.Id, n.Kind, n.ToName, n.ToAddress, n.Subject, n.Status, n.CreatedAt, n.SentAt, n.Attempts }),
                Can = new
                {
                    Decide = r.CanDecide(actor),
                    Answer = r.CanAnswer(actor),
                    Cancel = r.CanCancel(actor),
                    Order = r.CanOrder(actor),
                    Receive = r.CanReceive(actor),
                    Fulfil = r.CanFulfil(actor),
                    Comment = actor.CanRaise,
                },
                // Why someone who might expect to decide can't, so the page can say so.
                DecisionNote = r.Status == RequestStatus.PendingApproval && !r.CanDecide(actor)
                    ? (approver is null
                        ? "Waiting for an administrator to decide (no manager on record)."
                        : $"Waiting for {approver.DisplayName} (or an administrator) to decide.")
                      + (string.Equals(actor.Login, r.RequestedBy, StringComparison.OrdinalIgnoreCase)
                          && (actor.IsAdministrator || actor.PersonId == r.ApproverPersonId)
                          ? " You raised it, so you can't approve it yourself." : "")
                    : null,
            });
        });

        group.MapPost("", (NewDeviceRequest request, RequestService requests, ICurrentUser user, HttpContext http, IConfiguration config,
            CancellationToken ct) => Handle(async () =>
        {
            var created = await requests.SubmitAsync(request, await ActorAsync(requests, user, ct), LabelEndpoints.BaseUrl(http.Request, config), ct);
            return Results.Ok(new { created.Id, created.Reference, created.Status });
        })).AllowManagers();

        group.MapPost("/{id:guid}/decision", (Guid id, DecisionRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) => Handle(async () =>
        {
            var actor = await ActorAsync(requests, user, ct);
            var change = await requests.ActAsync(id, body.ExpectedStatus, actor, LabelEndpoints.BaseUrl(http.Request, config), (r, now) => body.Decision switch
            {
                Decision.Approve => r.Approve(actor, now, body.Comment, body.BudgetCode),
                Decision.Reject => r.Reject(actor, now, body.Comment ?? ""),
                Decision.AskForInfo => r.RequestInfo(actor, now, body.Comment ?? ""),
                _ => throw new DomainException("Choose approve, reject or ask for information."),
            }, ct);
            return Results.Ok(new { change.ToStatus });
        })).AllowManagers();

        group.MapPost("/{id:guid}/answer", (Guid id, TextRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) =>
            Act(id, body.ExpectedStatus, requests, user, http, config, (r, a, now) => r.Answer(a, now, body.Text ?? ""), ct)).AllowManagers();

        group.MapPost("/{id:guid}/cancel", (Guid id, TextRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) =>
            Act(id, body.ExpectedStatus, requests, user, http, config, (r, a, now) => r.Cancel(a, now, body.Text ?? ""), ct)).AllowManagers();

        group.MapPost("/{id:guid}/comment", (Guid id, TextRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) =>
            Act(id, null, requests, user, http, config, (r, a, now) => r.Comment(a, now, body.Text ?? ""), ct)).AllowManagers();

        group.MapPost("/{id:guid}/order", (Guid id, OrderRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) =>
            Act(id, body.ExpectedStatus, requests, user, http, config, (r, a, now) => r.Order(a, now, new PurchaseOrderDetails(
                body.Supplier ?? "", body.PurchaseOrder ?? "", body.Cost,
                body.OrderedOn ?? DateOnly.FromDateTime(now.ToLocalTime().DateTime), body.ExpectedDelivery, body.TrackingNumber, body.Notes)), ct));

        group.MapPost("/{id:guid}/receive", (Guid id, ReceiveRequest body, RequestService requests, ICurrentUser user, CancellationToken ct) =>
            Handle(async () =>
            {
                if (string.IsNullOrWhiteSpace(body.SerialNumber)) throw new DomainException("Enter the serial number.");
                if (string.IsNullOrWhiteSpace(body.Manufacturer) || string.IsNullOrWhiteSpace(body.Model))
                    throw new DomainException("Enter the manufacturer and model.");
                var details = new NewAsset(body.SerialNumber, body.Manufacturer, body.Model, body.CategoryId ?? Guid.Empty, body.AssetTag,
                    body.PurchaseDate, body.WarrantyExpiry, Cost: body.Cost, Notes: body.Notes);
                var asset = await requests.ReceiveAsync(id, body.ExpectedStatus, details, body.ReadyToIssue, body.LocationId,
                    await ActorAsync(requests, user, ct), ct);
                return Results.Ok(new { AssetId = asset.Id, asset.Status });
            }));

        group.MapPost("/{id:guid}/fulfil", (Guid id, FulfilRequest body, RequestService requests, ICurrentUser user, HttpContext http,
            IConfiguration config, CancellationToken ct) => Handle(async () =>
        {
            await requests.FulfilAsync(id, body.ExpectedStatus, body.AssetId, body.ExpectedAssetStatus, body.LocationId, body.Accessories ?? [],
                body.TicketNumber, await ActorAsync(requests, user, ct), LabelEndpoints.BaseUrl(http.Request, config), ct);
            return Results.Ok(new { Status = RequestStatus.Completed });
        }));
    }

    private static Task<IResult> Act(Guid id, RequestStatus? expectedStatus, RequestService requests, ICurrentUser user, HttpContext http,
        IConfiguration config, Func<DeviceRequest, RequestActor, DateTimeOffset, DeviceRequestEvent> action, CancellationToken ct) =>
        Handle(async () =>
        {
            var actor = await ActorAsync(requests, user, ct);
            var change = await requests.ActAsync(id, expectedStatus, actor, LabelEndpoints.BaseUrl(http.Request, config),
                (r, now) => action(r, actor, now), ct);
            return Results.Ok(new { change.ToStatus });
        });

    private static Task<RequestActor> ActorAsync(RequestService requests, ICurrentUser user, CancellationToken ct) =>
        requests.ActorAsync(user.Name, user.DisplayName, user.Email, user.ObjectId,
            user.IsInRole(Roles.Administrator), user.IsInRole(Roles.Technician), user.IsInRole(Roles.Manager), ct);

    /// <summary>Rule broken 400, not found 404, changed by someone else 409 (a duplicate serial says which asset has it).</summary>
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
        catch (Exception e) when (e is RequestChangedException or AssetChangedException)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DuplicateAssetException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["existingAssetId"] = e.ExistingAssetId });
        }
    }
}
