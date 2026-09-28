using Giim.Domain.Common;
using Giim.Domain.Locations;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal sealed record LocationRequest(string Name, LocationKind Kind, string? Address, bool HoldsStock, bool IsActive = true);

internal static class LocationEndpoints
{
    public static void MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locations");

        group.MapGet("/", async (GiimDbContext db, bool? activeOnly, CancellationToken ct) =>
        {
            var query = db.Locations.AsNoTracking();
            if (activeOnly == true) query = query.Where(l => l.IsActive);

            return Results.Ok(await query
                .OrderBy(l => l.Name)
                .Select(l => new
                {
                    l.Id, l.Name, l.Kind, l.Address, l.HoldsStock, l.IsActive,
                    Assets = db.Assets.Count(a => a.LocationId == l.Id && a.Status != Domain.Assets.AssetStatus.Disposed),
                    StockOnHand = db.StockMovements.Where(m => m.LocationId == l.Id).Sum(m => (int?)m.Quantity) ?? 0,
                })
                .ToListAsync(ct));
        });

        group.MapPost("/", async (LocationRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            string name;
            try { name = Location.CleanName(request.Name); }
            catch (DomainException e) { return Results.Problem(e.Message, statusCode: 400); }

            if (await db.Locations.AnyAsync(l => l.Name == name, ct))
                return Results.Problem($"There is already a location called '{name}'.", statusCode: 409);

            var location = new Location
            {
                Name = name,
                Kind = request.Kind,
                Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
                HoldsStock = request.HoldsStock,
                IsActive = request.IsActive,
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/locations/{location.Id}", location);
        });

        // Renaming here renames the location everywhere, because assets and stock link to it rather than copying the name.
        group.MapPut("/{id:guid}", async (Guid id, LocationRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            var location = await db.Locations.FindAsync([id], ct);
            if (location is null) return Results.NotFound();

            string name;
            try { name = Location.CleanName(request.Name); }
            catch (DomainException e) { return Results.Problem(e.Message, statusCode: 400); }

            if (await db.Locations.AnyAsync(l => l.Name == name && l.Id != id, ct))
                return Results.Problem($"There is already a location called '{name}'.", statusCode: 409);

            location.Name = name;
            location.Kind = request.Kind;
            location.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            location.HoldsStock = request.HoldsStock;
            location.IsActive = request.IsActive;
            location.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(location);
        });
    }
}
