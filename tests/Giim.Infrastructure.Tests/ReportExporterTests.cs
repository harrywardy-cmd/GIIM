using System.Text;
using ClosedXML.Excel;
using Giim.Infrastructure.Importing;
using Giim.Infrastructure.Reports;

namespace Giim.Infrastructure.Tests;

public class ReportExporterTests
{
    private static Report Sample(params (string Name, object? Cost, object? Warranty)[] rows) => new(
        "test", "Test report", "", [],
        [new("name", "Name"), new("cost", "Cost", ColumnType.Money), new("warranty", "Warranty ends", ColumnType.Date)],
        rows.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["name"] = r.Name, ["cost"] = r.Cost, ["warranty"] = r.Warranty,
        }).ToList());

    private static string[] CsvLines(Report report)
    {
        var bytes = ReportExporter.ToCsv(report);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);   // BOM, so Excel reads accented names correctly
        return Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToArray();
    }

    [Fact]
    public void Csv_has_a_header_and_australian_dates()
    {
        var lines = CsvLines(Sample(("Zoë's laptop", 1499.5m, new DateOnly(2027, 3, 1))));

        Assert.Equal("Name,Cost,Warranty ends", lines[0]);
        Assert.Equal("Zoë's laptop,1499.50,01/03/2027", lines[1]);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+61 400 000 000", "'+61 400 000 000")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    public void Csv_neutralises_formulas(string value, string expected)
    {
        Assert.Equal(expected + ",,", CsvLines(Sample((value, null, null)))[1]);
    }

    [Fact]
    public void Csv_leaves_negative_numbers_alone_and_quotes_commas()
    {
        var lines = CsvLines(Sample(("-12.5", -3m, null), ("Smith, Jo", null, null)));

        Assert.Equal("-12.5,-3.00,", lines[1]);
        Assert.Equal("\"Smith, Jo\",,", lines[2]);
    }

    [Fact]
    public void Excel_export_round_trips_with_typed_cells_and_text_formulas()
    {
        var bytes = ReportExporter.ToXlsx(Sample(("=1+1", 1499.5m, new DateOnly(2027, 3, 1)), ("Plain", null, null)));

        using (var stream = new MemoryStream(bytes))
        {
            var sheet = ExcelSheetReader.Read(stream);
            Assert.Equal(["Name", "Cost", "Warranty ends"], sheet.Headers);
            Assert.Equal(2, sheet.Rows.Count);
            Assert.Equal("Plain", sheet.Rows[1]["Name"]);
        }

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var cells = workbook.Worksheet(1);
        Assert.False(cells.Cell(2, 1).HasFormula);                 // "=1+1" stays as text
        Assert.Equal("=1+1", cells.Cell(2, 1).GetString());
        Assert.Equal(1499.5, cells.Cell(2, 2).GetDouble());        // money is a real number
        Assert.Equal(new DateTime(2027, 3, 1), cells.Cell(2, 3).GetDateTime());
        Assert.Equal("Test report", cells.Name);
    }

    [Fact]
    public void Excel_sheet_names_are_made_valid()
    {
        var report = Sample() with { Title = "Repairs: [period] 2026/27 with a very long report name" };

        using var workbook = new XLWorkbook(new MemoryStream(ReportExporter.ToXlsx(report)));

        Assert.Equal("Repairs period 202627 with a ve", workbook.Worksheet(1).Name);
    }
}
