using Giim.Domain.Assets;
using Giim.Infrastructure.Reports;

namespace Giim.Api.Endpoints;

/// <summary>Reports: the screen shows the first rows, exports (CSV or Excel) always contain every row.</summary>
internal static class ReportEndpoints
{
    private const int ScreenRows = 1000;

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports", () => ReportCatalogue.All.Select(r => new { r.Key, r.Title, r.Description, r.UsesDates }));

        app.MapGet("/api/reports/{key}", async (string key, ReportService reports, DateOnly? from, DateOnly? to, AssetStatus? status,
            Guid? categoryId, Guid? locationId, CancellationToken ct, int days = 90) =>
        {
            if (!Known(key)) return Results.NotFound();
            var filter = new ReportFilter(from, to, status, categoryId, locationId, days);
            var report = await reports.BuildAsync(key, filter, ct);
            var (periodFrom, periodTo) = reports.Period(filter);
            return Results.Ok(new
            {
                report.Key, report.Title, report.Description, report.Figures, report.Columns, report.ChartTitle, report.Chart,
                Rows = report.Rows.Take(ScreenRows),
                TotalRows = report.Rows.Count,
                PeriodFrom = periodFrom,
                PeriodTo = periodTo,
            });
        });

        app.MapGet("/api/reports/{key}/export", async (string key, ReportService reports, DateOnly? from, DateOnly? to, AssetStatus? status,
            Guid? categoryId, Guid? locationId, CancellationToken ct, string format = "xlsx", int days = 90) =>
        {
            if (!Known(key)) return Results.NotFound();
            if (format is not ("csv" or "xlsx")) return Results.Problem("Format must be csv or xlsx.", statusCode: 400);

            var report = await reports.BuildAsync(key, new ReportFilter(from, to, status, categoryId, locationId, days), ct);
            var name = $"GIIM {report.Title} {DateTime.Now:yyyy-MM-dd}.{format}";
            return format == "csv"
                ? Results.File(ReportExporter.ToCsv(report), "text/csv; charset=utf-8", name)
                : Results.File(ReportExporter.ToXlsx(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
        });
    }

    private static bool Known(string key) => ReportCatalogue.All.Any(r => r.Key == key);
}
