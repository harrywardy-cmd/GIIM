using Giim.Api.Security;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Infrastructure.People;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal sealed record ResolveOwnerRequest(Guid PersonId);

internal static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/people");

        group.MapPost("/sync", async (PeopleSyncService sync, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await sync.SyncAsync(ct));
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException)
            {
                return Results.Problem($"People sync failed: {e.Message}", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/", async (GiimDbContext db, string? search, PersonStatus? status, CancellationToken ct,
            int page = 1, int pageSize = 50) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var query = db.People.AsNoTracking();
            if (status is { } s) query = query.Where(p => p.Status == s);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.DisplayName.Contains(term) || p.EmployeeId.Contains(term)
                    || (p.UserPrincipalName != null && p.UserPrincipalName.Contains(term)));
            }

            var total = await query.CountAsync(ct);
            var people = await query
                .OrderBy(p => p.DisplayName).ThenBy(p => p.EmployeeId)
                .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
                .Select(p => new
                {
                    p.Id, p.EmployeeId, p.DisplayName, p.UserPrincipalName, Department = p.Department!.Name,
                    p.JobTitle, p.Location, p.Status,
                    AssetCount = db.Assets.Count(a => a.AssignedToPersonId == p.Id),
                    Manager = db.People.Where(m => m.Id == p.ManagerId).Select(m => new { m.Id, m.DisplayName }).FirstOrDefault(),
                })
                .ToListAsync(ct);

            return Results.Ok(new { total, people });
        });

        group.MapGet("/{id:guid}", async (Guid id, GiimDbContext db, CancellationToken ct) =>
        {
            var person = await db.People.AsNoTracking().Include(p => p.Department).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (person is null) return Results.NotFound();

            var manager = person.ManagerId is { } managerId
                ? await db.People.AsNoTracking().Where(p => p.Id == managerId).Select(p => new { p.Id, p.DisplayName }).FirstOrDefaultAsync(ct)
                : null;
            var reports = await db.People.AsNoTracking().CountAsync(p => p.ManagerId == id, ct);

            var assets = await db.Assets.AsNoTracking()
                .Where(a => a.AssignedToPersonId == id)
                .OrderBy(a => a.Category!.Name).ThenBy(a => a.AssetTag)
                .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name, a.Status })
                .ToListAsync(ct);

            var history = await (
                    from s in db.Assignments.AsNoTracking()
                    join a in db.Assets.AsNoTracking() on s.AssetId equals a.Id
                    where s.PersonId == id
                    orderby s.AssignedAt descending
                    select new { s.Id, AssetId = a.Id, a.AssetTag, a.Manufacturer, a.Model, s.AssignedAt, s.EndedAt, s.AssignedBy, s.Notes, s.ServiceDeskRequestId })
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                person.Id, person.EmployeeId, person.DisplayName, person.UserPrincipalName, person.Email,
                Department = person.Department?.Name, person.JobTitle, person.Location, person.Status, person.Track,
                person.StartDate, person.EndDate, person.LastSyncedAt, Manager = manager, DirectReports = reports,
                Assets = assets, History = history,
            });
        });

        group.MapGet("/legacy-owners", async (LegacyOwnerService legacy, CancellationToken ct, int take = 200) =>
        {
            var preview = await legacy.PreviewAsync(ct);
            return Results.Ok(new
            {
                preview.Unlinked, preview.CanLinkAutomatically, preview.Ambiguous, preview.NotFound,
                Unresolved = preview.Unresolved.Take(Math.Clamp(take, 1, 1000)),
            });
        });

        group.MapPost("/legacy-owners/link-automatic", async (LegacyOwnerService legacy, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(new { Linked = await legacy.LinkAutomaticAsync(user.Name, ct) }));

        group.MapPost("/legacy-owners/{assetId:guid}/resolve", async (Guid assetId, ResolveOwnerRequest request,
            LegacyOwnerService legacy, ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                await legacy.ResolveAsync(assetId, request.PersonId, user.Name, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (DomainException e)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        app.MapGet("/api/departments", async (GiimDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Departments.AsNoTracking().OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Code, d.Name, People = db.People.Count(p => p.DepartmentId == d.Id) })
                .ToListAsync(ct)));
    }
}
