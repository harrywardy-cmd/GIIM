using Giim.Domain.Importing;
using Giim.Infrastructure.Activity;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

/// <summary>Tickets view, technician activity and the global search behind the top bar.</summary>
internal static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tickets", async (ActivityService activity, string? search, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (total, tickets) = await activity.TicketsAsync(search, page, pageSize, ct);
            return Results.Ok(new { total, tickets });
        });

        app.MapGet("/api/activity", async (ActivityService activity, string? ticket, string? actor, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(ticket) && string.IsNullOrWhiteSpace(actor))
                return Results.Problem("Give a ticket number or a technician.", statusCode: 400);
            return Results.Ok(await activity.FeedAsync(ticket, actor, ct));
        });

        // One box, everything: assets, people, tickets and technicians. An exact serial/tag or ticket is flagged
        // so the UI can open it straight away.
        app.MapGet("/api/search", async (string q, GiimDbContext db, ActivityService activity, AssetLifecycleService lifecycle,
            CancellationToken ct) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length < 2) return Results.Ok(new { term, exactAssetId = (Guid?)null, exactTicket = (string?)null, assets = Array.Empty<object>(), people = Array.Empty<object>(), tickets = Array.Empty<object>(), technicians = Array.Empty<object>() });

            var exactAssetId = await lifecycle.LookupAsync(term, ct);
            var serialTerm = ImportNormalizer.Serial(term) ?? term;

            var assets = await db.Assets.AsNoTracking()
                .Where(a => a.SerialNumber.Contains(serialTerm) || (a.AssetTag != null && a.AssetTag.Contains(term))
                    || a.Model.Contains(term) || a.Manufacturer.Contains(term))
                .OrderBy(a => a.AssetTag).ThenBy(a => a.SerialNumber)
                .Take(8)
                .Select(a => new
                {
                    a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name, a.Status,
                    AssignedTo = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.DisplayName).FirstOrDefault(),
                })
                .ToListAsync(ct);

            var people = await db.People.AsNoTracking()
                .Where(p => p.DisplayName.Contains(term) || p.EmployeeId.Contains(term) || (p.UserPrincipalName != null && p.UserPrincipalName.Contains(term)))
                .OrderBy(p => p.DisplayName)
                .Take(8)
                .Select(p => new
                {
                    p.Id, p.DisplayName, p.UserPrincipalName, Department = p.Department!.Name, p.Status,
                    Assets = db.Assets.Count(a => a.AssignedToPersonId == p.Id),
                })
                .ToListAsync(ct);

            var (_, tickets) = await activity.TicketsAsync(term, 1, 8, ct);
            var exactTicket = tickets.FirstOrDefault(t => t.TicketNumber == ActivityService.NormaliseTicket(term))?.TicketNumber;
            var technicians = (await activity.TechniciansAsync(term, 5, ct)).Select(t => new { t.Actor, t.Actions });

            return Results.Ok(new { term, exactAssetId, exactTicket, assets, people, tickets, technicians });
        });
    }
}
