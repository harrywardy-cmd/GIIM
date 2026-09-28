using System.Text.Json.Serialization;
using Giim.Infrastructure;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddHealthChecks().AddDbContextCheck<GiimDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");

var assets = app.MapGroup("/api/assets");

assets.MapGet("/", async (GiimDbContext db, string? search, int page = 1, int pageSize = 50) =>
{
    pageSize = Math.Clamp(pageSize, 1, 200);
    var query = db.Assets.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(a => a.SerialNumber.Contains(search) || (a.AssetTag != null && a.AssetTag.Contains(search)));

    var items = await query
        .OrderBy(a => a.SerialNumber)
        .Skip((Math.Max(page, 1) - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(items);
});

app.Run();
