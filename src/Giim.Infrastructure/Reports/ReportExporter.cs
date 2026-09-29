using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Giim.Infrastructure.Reports;

/// <summary>Writes a report to CSV or Excel. Exports always contain every row, unlike the on-screen view.</summary>
public static class ReportExporter
{
    private static readonly CultureInfo Australian = CultureInfo.GetCultureInfo("en-AU");

    public static byte[] ToCsv(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', report.Columns.Select(c => Field(c.Label))));
        foreach (var row in report.Rows)
            csv.AppendLine(string.Join(',', report.Columns.Select(c => Field(Format(row.GetValueOrDefault(c.Key), c.Type)))));

        // UTF-8 with a byte-order mark so Excel opens names like "Zoë" correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public static byte[] ToXlsx(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(report.Title));

        for (var c = 0; c < report.Columns.Count; c++)
            sheet.Cell(1, c + 1).Value = report.Columns[c].Label;

        var r = 2;
        foreach (var row in report.Rows)
        {
            for (var c = 0; c < report.Columns.Count; c++)
            {
                var column = report.Columns[c];
                var cell = sheet.Cell(r, c + 1);
                switch (row.GetValueOrDefault(column.Key))
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd/mm/yyyy"; break;
                    case DateTimeOffset dt: cell.Value = dt.ToLocalTime().DateTime; cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm"; break;
                    case decimal m: cell.Value = m; if (column.Type == ColumnType.Money) cell.Style.NumberFormat.Format = "$#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case double dbl: cell.Value = dbl; break;
                    // Strings are written as text, never as formulas, so "=cmd|..." can't execute in Excel.
                    case var other: cell.Value = other.ToString(); break;
                }
            }
            r++;
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        if (report.Rows.Count > 0)
            sheet.Range(1, 1, r - 1, report.Columns.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(r - 1, 500));   // sizing from the first rows keeps big exports fast

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    internal static string Format(object? value, ColumnType type) => value switch
    {
        null => "",
        DateOnly d => d.ToString("dd/MM/yyyy", Australian),
        DateTimeOffset dt => dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Australian),
        decimal m when type == ColumnType.Money => m.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// Quotes a CSV field, and neutralises text a spreadsheet would treat as a formula (CSV injection):
    /// a leading =, +, -, @, tab or carriage return gets a leading apostrophe. Plain negative numbers are left alone.
    /// </summary>
    internal static string Field(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal)
            && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static string SheetName(string title)
    {
        var cleaned = new string(title.Where(ch => !"[]:*?/\\".Contains(ch, StringComparison.Ordinal)).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
