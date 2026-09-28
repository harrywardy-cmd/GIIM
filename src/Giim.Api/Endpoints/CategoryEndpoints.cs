using Giim.Domain.Assets;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Api.Endpoints;

internal sealed record CategoryRequest(string Name, bool IsIntuneManaged, bool ReturnOnOffboarding, bool IsActive = true);

internal static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories");

        group.MapGet("/", async (GiimDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Assets.GroupBy(a => a.CategoryId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var categories = await db.AssetCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

            return Results.Ok(categories.Select(c => new
            {
                c.Id, c.Name, c.IsIntuneManaged, c.ReturnOnOffboarding, c.IsActive,
                AssetCount = counts.GetValueOrDefault(c.Id),
            }));
        });

        group.MapPost("/", async (CategoryRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            if (await Validate(request, null, db, ct) is { } problem) return problem;

            var category = new AssetCategory
            {
                Name = request.Name.Trim(),
                IsIntuneManaged = request.IsIntuneManaged,
                ReturnOnOffboarding = request.ReturnOnOffboarding,
                IsActive = request.IsActive,
            };
            db.AssetCategories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/categories/{category.Id}", category);
        });

        group.MapPut("/{id:guid}", async (Guid id, CategoryRequest request, GiimDbContext db, CancellationToken ct) =>
        {
            var category = await db.AssetCategories.FindAsync([id], ct);
            if (category is null) return Results.NotFound();
            if (await Validate(request, id, db, ct) is { } problem) return problem;

            category.Name = request.Name.Trim();
            category.IsIntuneManaged = request.IsIntuneManaged;
            category.ReturnOnOffboarding = request.ReturnOnOffboarding;
            category.IsActive = request.IsActive;
            category.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(category);
        });
    }

    private static async Task<IResult?> Validate(CategoryRequest request, Guid? id, GiimDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            return Results.Problem("Name is required (up to 100 characters).", statusCode: 400);
        if (await db.AssetCategories.AnyAsync(c => c.Name == name && c.Id != id, ct))
            return Results.Problem($"A category called '{name}' already exists.", statusCode: 409);
        return null;
    }
}
