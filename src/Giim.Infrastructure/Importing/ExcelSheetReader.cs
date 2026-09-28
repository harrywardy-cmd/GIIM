using System.Globalization;
using ClosedXML.Excel;

namespace Giim.Infrastructure.Importing;

public sealed record SheetData(string SheetName, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

/// <summary>
/// Reads the first worksheet of an .xlsx file into header-keyed rows of text.
/// Real Excel date cells become ISO dates (yyyy-MM-dd) so parsing doesn't depend on the PC's regional settings.
/// </summary>
public static class ExcelSheetReader
{
    public const int MaxRows = 200_000;

    public static SheetData Read(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed() ?? throw new InvalidDataException("The worksheet is empty.");

        var headerRow = used.FirstRow();
        var headers = new List<(int Column, string Name)>();
        foreach (var cell in headerRow.Cells())
        {
            var name = cell.GetString().Trim();
            if (name.Length == 0) continue;
            if (headers.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Column heading '{name}' appears more than once.");
            headers.Add((cell.Address.ColumnNumber, name));
        }

        if (headers.Count == 0) throw new InvalidDataException("The first row has no column headings.");

        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            if (rows.Count >= MaxRows)
                throw new InvalidDataException($"The sheet has more than {MaxRows:N0} rows.");

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, name) in headers)
                values[name] = CellText(row.WorksheetRow().Cell(column));

            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(values);
        }

        return new SheetData(sheet.Name, headers.Select(h => h.Name).ToList(), rows);
    }

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;

        return cell.DataType switch
        {
            XLDataType.DateTime => DateOnly.FromDateTime(cell.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            _ => cell.GetFormattedString(),
        };
    }
}
