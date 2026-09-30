using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Giim.Api.Endpoints;

internal sealed record LabelRequest(IReadOnlyList<Guid> AssetIds);

/// <summary>
/// QR codes and label data. A QR code holds a link to the asset in GIIM, so a phone camera or a 2D barcode
/// scanner opens the asset directly. The link uses Giim:PublicBaseUrl (the address staff use to reach GIIM);
/// when that isn't set, the address of the current request.
/// </summary>
internal static class LabelEndpoints
{
    private const int MaxLabels = 500;

    public static void MapLabelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets");

        group.MapGet("/{id:guid}/qr.svg", async (Guid id, GiimDbContext db, HttpRequest request, IConfiguration config, CancellationToken ct) =>
        {
            if (!await db.Assets.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
            return Results.Text(Svg(Link(request, config, id)), "image/svg+xml");
        });

        group.MapGet("/{id:guid}/qr.png", async (Guid id, GiimDbContext db, HttpRequest request, IConfiguration config, CancellationToken ct) =>
        {
            var asset = await db.Assets.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new { a.AssetTag, a.SerialNumber }).FirstOrDefaultAsync(ct);
            if (asset is null) return Results.NotFound();

            return Results.File(Png(Link(request, config, id)), "image/png", $"{asset.AssetTag ?? asset.SerialNumber}-qr.png");
        });

        group.MapPost("/labels", async (LabelRequest body, GiimDbContext db, HttpRequest request, IConfiguration config, CancellationToken ct) =>
        {
            var ids = (body.AssetIds ?? []).Distinct().ToList();
            if (ids.Count == 0) return Results.Problem("Choose at least one asset.", statusCode: 400);
            if (ids.Count > MaxLabels) return Results.Problem($"Print at most {MaxLabels} labels at a time.", statusCode: 400);

            var assets = await db.Assets.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => new { a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name })
                .ToListAsync(ct);

            // Keep the order the user chose, so labels come out in the order they were ticked.
            var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            return Results.Ok(assets.OrderBy(a => order[a.Id]).Select(a => new
            {
                a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, a.Category,
                Link = Link(request, config, a.Id),
                Qr = Svg(Link(request, config, a.Id)),
            }));
        });
    }

    internal static string Link(HttpRequest request, IConfiguration config, Guid assetId) => $"{BaseUrl(request, config)}/?asset={assetId}";

    /// <summary>The address staff use to open GIIM (Giim:PublicBaseUrl), or the one this request came in on.</summary>
    internal static string BaseUrl(HttpRequest request, IConfiguration config)
    {
        var configured = config["Giim:PublicBaseUrl"];
        return string.IsNullOrWhiteSpace(configured) ? $"{request.Scheme}://{request.Host}" : configured.TrimEnd('/');
    }

    private static string Svg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(4);
    }

    private static byte[] Png(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(12);
    }
}
