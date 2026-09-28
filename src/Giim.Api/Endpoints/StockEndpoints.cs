using Giim.Api.Security;
using Giim.Domain.Common;
using Giim.Domain.Stock;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal sealed record StockItemRequest(string Name, string? Description, int ReorderLevel, bool IsActive = true);

internal sealed record StockMovementRequest(
    Guid LocationId, StockMovementReason Reason, int Quantity, string? Note, string? ServiceDeskRequestId);

internal static class StockEndpoints
{
    public static void MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock");

        group.MapGet("/", async (StockService stock, CancellationToken ct) => Results.Ok(await stock.GetLevelsAsync(ct)));

        // Places stock can be held: locations marked "holds stock", plus any that still have stock on the books.
        group.MapGet("/locations", async (GiimDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Locations.AsNoTracking()
                .Where(l => (l.IsActive && l.HoldsStock) || db.StockMovements.Where(m => m.LocationId == l.Id).Sum(m => m.Quantity) != 0)
                .OrderBy(l => l.Name)
                .Select(l => new { l.Id, l.Name, l.IsActive })
                .ToListAsync(ct)));

        group.MapPost("/items", async (StockItemRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            if (await Validate(request, null, db, ct) is { } problem) return problem;

            var item = new StockItem
            {
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                ReorderLevel = request.ReorderLevel,
                IsActive = request.IsActive,
            };
            db.StockItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/stock/items/{item.Id}", item);
        });

        group.MapPut("/items/{id:guid}", async (Guid id, StockItemRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            var item = await db.StockItems.FindAsync([id], ct);
            if (item is null) return Results.NotFound();
            if (await Validate(request, id, db, ct) is { } problem) return problem;

            item.Name = request.Name.Trim();
            item.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            item.ReorderLevel = request.ReorderLevel;
            item.IsActive = request.IsActive;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });

        group.MapGet("/items/{id:guid}/movements", async (Guid id, StockService stock, CancellationToken ct) =>
            Results.Ok(await stock.GetHistoryAsync(id, ct)));

        group.MapPost("/items/{id:guid}/movements", async (Guid id, StockMovementRequest request, StockService stock,
            ICurrentUser user, CancellationToken ct) =>
        {
            var actor = user.Name;
            try
            {
                var movement = await stock.RecordAsync(id, request.LocationId, request.Reason, request.Quantity,
                    actor, request.Note, request.ServiceDeskRequestId, ct);
                return Results.Ok(movement);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DomainException e)
            {
                return Results.Problem(e.Message, statusCode: 400);
            }
        });
    }

    private static async Task<IResult?> Validate(StockItemRequest request, Guid? id, GiimDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 150)
            return Results.Problem("Name is required (up to 150 characters).", statusCode: 400);
        if (request.ReorderLevel < 0)
            return Results.Problem("Reorder level cannot be negative.", statusCode: 400);
        if (await db.StockItems.AnyAsync(s => s.Name == name && s.Id != id, ct))
            return Results.Problem($"A stock item called '{name}' already exists.", statusCode: 409);
        return null;
    }
}
