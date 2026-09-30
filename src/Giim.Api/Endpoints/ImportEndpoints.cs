using System.Text.Json;
using Giim.Api.Security;
using Giim.Domain.Importing;
using Giim.Infrastructure.Importing;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Giim.Api.Endpoints;

internal static class ImportEndpoints
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int MaxRowsReturned = 500;

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        // Antiforgery tokens aren't used: every change must carry the X-GIIM-Request header instead (AuthSetup.UseCsrfHeaderCheck).
        var group = app.MapGroup("/api/imports/assets").DisableAntiforgery();

        group.MapPost("/preview", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping,
            AssetImportService imports, IOptions<JsonOptions> json, CancellationToken ct) =>
        {
            if (Validate(file) is { } problem) return problem;

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await imports.PreviewAsync(stream, ParseMapping(mapping, json.Value.SerializerOptions), ct);
                return Results.Ok(ToResponse(result));
            }
            catch (Exception e) when (e is InvalidDataException or JsonException)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/commit", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string mapping,
            AssetImportService imports, IOptions<JsonOptions> json, ICurrentUser user, CancellationToken ct) =>
        {
            if (Validate(file) is { } problem) return problem;

            try
            {
                var columnMapping = ParseMapping(mapping, json.Value.SerializerOptions)
                    ?? throw new InvalidDataException("A column mapping is required to import.");
                var actor = user.Name;

                await using var stream = file.OpenReadStream();
                var result = await imports.CommitAsync(stream, columnMapping, file.FileName, actor, ct);
                return Results.Ok(ToResponse(result));
            }
            catch (Exception e) when (e is InvalidDataException or JsonException)
            {
                return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });
    }

    private static IResult? Validate(IFormFile file)
    {
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Only .xlsx files are supported. Save the sheet as an Excel Workbook first.", statusCode: 400);
        if (file.Length > MaxUploadBytes)
            return Results.Problem("The file is larger than 20 MB.", statusCode: 400);
        return null;
    }

    private static ColumnMapping? ParseMapping(string? mappingJson, JsonSerializerOptions json) =>
        string.IsNullOrWhiteSpace(mappingJson)
            ? null
            : new ColumnMapping { Columns = JsonSerializer.Deserialize<Dictionary<AssetField, string>>(mappingJson, json) ?? [] };

    private static object ToResponse(AssetImportResult result) => new
    {
        result.SheetName,
        result.Headers,
        Mapping = result.Mapping.Columns,
        result.Summary,
        result.NewLocations,
        // The UI needs the rows that need attention; clean rows are summarised by the counts.
        Rows = result.Rows
            .Where(r => r.Outcome != ImportRowOutcome.New || r.Issues.Count > 0)
            .Take(MaxRowsReturned)
            .Select(r => new { r.RowNumber, r.Outcome, r.Issues, r.SerialNumber, r.AssetTag, r.Manufacturer, r.Model }),
    };
}
