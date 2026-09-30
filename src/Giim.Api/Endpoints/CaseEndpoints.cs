using Giim.Api.Security;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Provisioning;
using Giim.Infrastructure.Cases;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Provisioning;
using Giim.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal sealed record OffboardingRequest(Guid PersonId, DateOnly? LastDay, string? TicketNumber, string? Notes);
internal sealed record CaseTextRequest(string? Text);
internal sealed record RaiseRequestBody(Guid? CategoryId, string? DeviceDescription);
internal sealed record ProfileRequest(Guid DepartmentId, string? Name, string? JobTitle, ProvisioningTrack Track);
internal sealed record ProfileItemRequest(ProfileItemType Type, string? Description, string? GroupName, Guid? CategoryId, Guid? StockItemId);

/// <summary>
/// Starter and leaver checklists (technicians and administrators), and the starter profiles they come from
/// (administrators change them; everyone can read them).
/// </summary>
internal static class CaseEndpoints
{
    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var cases = app.MapGroup("/api/cases");

        cases.MapGet("", async (CaseService service, CaseView? view, string? search, Guid? personId, CancellationToken ct) =>
        {
            var (items, counts) = await service.ListAsync(view ?? CaseView.Starters, search, personId, ct);
            return Results.Ok(new { items, counts });
        });

        cases.MapGet("/{id:guid}", async (Guid id, CaseService service, GiimDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            if (await service.GetAsync(id, ct) is not { } c) return Results.NotFound();

            var person = await db.People.AsNoTracking().Where(p => p.Id == c.PersonId)
                .Select(p => new
                {
                    p.Id, p.DisplayName, p.EmployeeId, p.JobTitle, p.Status, p.Track, Email = p.Email ?? p.UserPrincipalName,
                    Department = p.Department!.Name,
                    Manager = db.People.Where(m => m.Id == p.ManagerId).Select(m => m.DisplayName).FirstOrDefault(),
                })
                .FirstAsync(ct);

            // What each task points at: the device request raised for it, or the asset to recover.
            var requestIds = c.Tasks.Where(t => t.DeviceRequestId != null).Select(t => t.DeviceRequestId!.Value).ToList();
            var requests = await db.DeviceRequests.AsNoTracking().Where(r => requestIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Number, r.Status }).ToDictionaryAsync(r => r.Id, ct);
            var assetIds = c.Tasks.Where(t => t.Source == TaskSource.Asset && t.SourceId != null).Select(t => t.SourceId!.Value).ToList();
            var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Status, StillHeld = a.AssignedToPersonId == c.PersonId })
                .ToDictionaryAsync(a => a.Id, ct);
            var profile = c.RoleProfileId is { } profileId
                ? await db.RoleProfiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.Name).FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(new
            {
                c.Id, c.Type, c.Status, c.DueDate, TicketNumber = c.ServiceDeskRequestId, c.Notes, c.CreatedAt, c.CreatedBy,
                c.CompletedAt, c.CancellationReason, c.CancelledBy, Profile = profile, Person = person,
                Tasks = c.Tasks.OrderBy(t => t.Order).Select(t => new
                {
                    t.Id, t.Order, t.Title, t.Kind, t.Status, t.RequiresApproval, t.ApprovedBy, t.ApprovedAt, t.Source, t.SourceId,
                    t.CategoryId, t.CompletedBy, t.CompletedAt, t.Notes,
                    Request = t.DeviceRequestId is { } r && requests.TryGetValue(r, out var request) ? request : null,
                    Asset = t.Source == TaskSource.Asset && t.SourceId is { } a && assets.TryGetValue(a, out var asset) ? asset : null,
                }),
                Can = new { Approve = user.IsInRole(Roles.Administrator) },
            });
        });

        cases.MapPost("/onboarding", (StarterDetails body, CaseService service, ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var created = await service.StartOnboardingAsync(body, user.Name, ct);
            return Results.Ok(new { created.Id });
        }));

        cases.MapPost("/offboarding", (OffboardingRequest body, CaseService service, ICurrentUser user, CancellationToken ct) => Handle(async () =>
        {
            var created = await service.StartOffboardingAsync(body.PersonId,
                body.LastDay ?? throw new DomainException("Enter their last day."), body.TicketNumber, body.Notes, user.Name, ct);
            return Results.Ok(new { created.Id });
        }));

        cases.MapPost("/{id:guid}/tasks/{taskId:guid}/complete", (Guid id, Guid taskId, CaseTextRequest body, CaseService service,
            ICurrentUser user, CancellationToken ct) => Act(service, id, (c, now) => c.CompleteTask(taskId, user.Name, body.Text, now), ct));

        cases.MapPost("/{id:guid}/tasks/{taskId:guid}/skip", (Guid id, Guid taskId, CaseTextRequest body, CaseService service,
            ICurrentUser user, CancellationToken ct) => Act(service, id, (c, now) => c.SkipTask(taskId, user.Name, body.Text ?? "", now), ct));

        cases.MapPost("/{id:guid}/tasks/{taskId:guid}/reopen", (Guid id, Guid taskId, CaseService service, ICurrentUser user,
            CancellationToken ct) => Act(service, id, (c, now) => c.ReopenTask(taskId, user.Name, now), ct));

        cases.MapPost("/{id:guid}/tasks/{taskId:guid}/approve", (Guid id, Guid taskId, CaseService service, ICurrentUser user,
            CancellationToken ct) => Act(service, id, (c, now) => c.ApproveTask(taskId, user.Name, user.IsInRole(Roles.Administrator), now), ct));

        cases.MapPost("/{id:guid}/tasks", (Guid id, CaseTextRequest body, CaseService service, ICurrentUser user, CancellationToken ct) =>
            Act(service, id, (c, now) => c.AddTask(body.Text ?? "", user.Name, now), ct));

        cases.MapPost("/{id:guid}/cancel", (Guid id, CaseTextRequest body, CaseService service, ICurrentUser user, CancellationToken ct) =>
            Act(service, id, (c, now) => c.Cancel(user.Name, body.Text ?? "", now), ct));

        cases.MapPost("/{id:guid}/tasks/{taskId:guid}/device-request", (Guid id, Guid taskId, RaiseRequestBody body, CaseService service,
            RequestService requests, ICurrentUser user, HttpContext http, IConfiguration config, CancellationToken ct) => Handle(async () =>
        {
            var actor = await requests.ActorAsync(user.Name, user.DisplayName, user.Email, user.ObjectId,
                user.IsInRole(Roles.Administrator), user.IsInRole(Roles.Technician), user.IsInRole(Roles.Manager), ct);
            var request = await service.RaiseDeviceRequestAsync(id, taskId, body.CategoryId, body.DeviceDescription, actor,
                LabelEndpoints.BaseUrl(http.Request, config), ct);
            return Results.Ok(new { request.Id, request.Reference, request.Status });
        }));

        MapProfiles(app);
    }

    private static void MapProfiles(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api/profiles");

        profiles.MapGet("", async (GiimDbContext db, CancellationToken ct) =>
        {
            var list = await db.Departments.AsNoTracking().OrderBy(d => d.Name)
                .Select(d => new
                {
                    DepartmentId = d.Id, Department = d.Name, d.Code,
                    Profiles = db.RoleProfiles.Where(p => p.DepartmentId == d.Id).OrderBy(p => p.JobTitle).ThenBy(p => p.Name)
                        .Select(p => new { p.Id, p.Name, p.JobTitle, p.Track, Items = p.Items.Count }).ToList(),
                })
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        profiles.MapGet("/{id:guid}", async (Guid id, GiimDbContext db, CancellationToken ct) =>
        {
            var profile = await db.RoleProfiles.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id, p.Name, p.JobTitle, p.Track, p.DepartmentId,
                    Department = db.Departments.Where(d => d.Id == p.DepartmentId).Select(d => d.Name).First(),
                    Items = p.Items.OrderBy(i => i.CreatedAt).Select(i => new
                    {
                        i.Id, i.Type, i.Description, i.GroupName, i.CategoryId, i.StockItemId,
                        Category = db.AssetCategories.Where(c => c.Id == i.CategoryId).Select(c => c.Name).FirstOrDefault(),
                        StockItem = db.StockItems.Where(s => s.Id == i.StockItemId).Select(s => s.Name).FirstOrDefault(),
                    }).ToList(),
                })
                .FirstOrDefaultAsync(ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        profiles.MapPost("", (ProfileRequest body, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            var created = await service.CreateAsync(body.DepartmentId, new ProfileDetails(body.Name ?? "", body.JobTitle, body.Track), ct);
            return Results.Ok(new { created.Id });
        }));

        profiles.MapPut("/{id:guid}", (Guid id, ProfileRequest body, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            await service.UpdateAsync(id, new ProfileDetails(body.Name ?? "", body.JobTitle, body.Track), ct);
            return Results.NoContent();
        }));

        profiles.MapDelete("/{id:guid}", (Guid id, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        }));

        profiles.MapPost("/{id:guid}/items", (Guid id, ProfileItemRequest body, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            var item = await service.AddItemAsync(id, Item(body), ct);
            return Results.Ok(new { item.Id });
        }));

        profiles.MapPut("/{id:guid}/items/{itemId:guid}", (Guid id, Guid itemId, ProfileItemRequest body, ProfileService service,
            CancellationToken ct) => Handle(async () =>
        {
            await service.UpdateItemAsync(id, itemId, Item(body), ct);
            return Results.NoContent();
        }));

        profiles.MapDelete("/{id:guid}/items/{itemId:guid}", (Guid id, Guid itemId, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            await service.DeleteItemAsync(id, itemId, ct);
            return Results.NoContent();
        }));

        // Bulk set-up from a JSON file (the samples/departments-and-profiles.json format).
        profiles.MapPost("/import", (IFormFile file, ProfileService service, CancellationToken ct) => Handle(async () =>
        {
            if (file.Length > 5 * 1024 * 1024) throw new DomainException("That file is too large (5 MB maximum).");
            await using var stream = file.OpenReadStream();
            try
            {
                return Results.Ok(await service.ImportAsync(stream, ct));
            }
            catch (System.Text.Json.JsonException)
            {
                throw new DomainException("That file isn't valid JSON in the starter profile format.");
            }
            catch (KeyNotFoundException)
            {
                throw new DomainException("Each department needs a “department” (code and name) and “profiles”, each with “items”.");
            }
        })).DisableAntiforgery();
    }

    private static ProfileItemDetails Item(ProfileItemRequest body) =>
        new(body.Type, body.Description ?? "", body.GroupName, body.CategoryId, body.StockItemId);

    private static Task<IResult> Act(CaseService service, Guid id, Action<ServiceCase, DateTimeOffset> action, CancellationToken ct) =>
        Handle(async () =>
        {
            await service.ActAsync(id, action, ct);
            return Results.NoContent();
        });

    /// <summary>Rule broken 400, not found 404, changed by someone else 409.</summary>
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
        catch (Exception e) when (e is CaseChangedException or RequestChangedException)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
