namespace Giim.Infrastructure.Reports;

public enum ColumnType { Text, Number, Money, Date, DateTime }

public sealed record ReportColumn(string Key, string Label, ColumnType Type = ColumnType.Text);

public sealed record ReportFigure(string Label, string Value);

public sealed record ChartBar(string Label, decimal Value);

/// <summary>A report ready for the screen or for export: key figures, an optional bar chart and a table.</summary>
public sealed record Report(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<ReportFigure> Figures,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string? ChartTitle = null,
    IReadOnlyList<ChartBar>? Chart = null);

/// <summary>Filters shared by the reports; each report uses the ones that make sense for it.</summary>
public sealed record ReportFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    Domain.Assets.AssetStatus? Status = null,
    Guid? CategoryId = null,
    Guid? LocationId = null,
    int Days = 90);

public static class ReportCatalogue
{
    public static readonly IReadOnlyList<(string Key, string Title, string Description, bool UsesDates)> All =
    [
        ("inventory", "Asset inventory", "Every asset with status, location and holder.", false),
        ("warranty", "Warranty", "Warranties expiring soon, already expired, or not recorded.", false),
        ("repairs", "Repairs", "Repairs opened in the period: cost, turnaround, vendor and repeat faults.", true),
        ("technicians", "Technician activity", "What each technician recorded in the period.", true),
        ("leavers", "Leavers holding kit", "People who have left or are leaving and still have assets recorded against them.", false),
        ("stock", "Stock levels", "Stock on hand per location against reorder levels.", false),
    ];
}
