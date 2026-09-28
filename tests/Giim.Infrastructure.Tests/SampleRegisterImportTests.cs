using System.Globalization;
using System.Text.RegularExpressions;
using Giim.Domain.Assets;
using Giim.Domain.Importing;
using Giim.Infrastructure.Importing;

namespace Giim.Infrastructure.Tests;

/// <summary>
/// Runs the importer over the generated fake register and checks it finds exactly the problems
/// the generator planted (samples/EXPECTED-ISSUES.md). Regenerating the samples keeps this in sync.
/// </summary>
public partial class SampleRegisterImportTests
{
    private static readonly string SamplesDir = Path.Combine(FindRepoRoot(), "samples");

    private static readonly Lazy<IReadOnlyList<AnalyzedRow>> Rows = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(SamplesDir, "legacy-asset-register.xlsx"));
        var sheet = ExcelSheetReader.Read(file);
        var categories = AssetCategory.Defaults.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        return AssetImportAnalyzer.Analyze(sheet.Rows, ColumnMapping.Suggest(sheet.Headers), new HashSet<string>(), new HashSet<string>(), categories);
    });

    private static readonly Lazy<Dictionary<string, int>> Expected = new(() =>
        File.ReadLines(Path.Combine(SamplesDir, "EXPECTED-ISSUES.md"))
            .Select(line => TableRow().Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups["label"].Value.Trim(), m => int.Parse(m.Groups["count"].Value, CultureInfo.InvariantCulture)));

    private static int Count(ImportIssue issue, bool excludeDuplicates = false) =>
        Rows.Value.Count(r => r.Issues.Contains(issue) && !(excludeDuplicates && r.Issues.Contains(ImportIssue.DuplicateSerialInFile)));

    [Fact]
    public void Every_row_is_read()
    {
        var expected = Expected.Value["Rows in Excel register (devices)"] + Expected.Value["Excel: Duplicate rows (same serial twice)"];
        Assert.Equal(expected, Rows.Value.Count);
    }

    [Fact]
    public void Blank_serials_are_found()
    {
        Assert.Equal(Expected.Value["Excel: Blank serial number"], Count(ImportIssue.BlankSerial));
    }

    [Fact]
    public void Duplicate_serials_are_found()
    {
        Assert.Equal(Expected.Value["Excel: Duplicate rows (same serial twice)"], Count(ImportIssue.DuplicateSerialInFile));
    }

    // The generator counts devices; the importer counts rows. Duplicate rows repeat the same mess, so exclude them.
    [Fact]
    public void Messy_serials_are_cleaned()
    {
        Assert.Equal(Expected.Value["Excel: Serial with stray spaces / lowercase"], Count(ImportIssue.SerialCleaned, excludeDuplicates: true));
    }

    [Fact]
    public void Manufacturer_variants_are_unified()
    {
        Assert.Equal(Expected.Value["Excel: Make spelled differently (e.g. Hewlett-Packard)"], Count(ImportIssue.ManufacturerRenamed, excludeDuplicates: true));
        Assert.DoesNotContain(Rows.Value, r => r.Manufacturer == "Hewlett-Packard");
    }

    [Fact]
    public void Only_planted_problems_cause_rejections()
    {
        var rejected = Rows.Value.Where(r => r.Outcome == ImportRowOutcome.Rejected).ToList();

        Assert.All(rejected, r => Assert.True(
            r.Issues.Contains(ImportIssue.BlankSerial) || r.Issues.Contains(ImportIssue.DuplicateSerialInFile),
            $"Row {r.RowNumber} rejected for: {string.Join(", ", r.Issues)}"));
    }

    [Fact]
    public void Text_and_real_dates_are_both_read()
    {
        Assert.DoesNotContain(Rows.Value, r => r.Issues.Contains(ImportIssue.InvalidDate));
        Assert.All(Rows.Value.Where(r => r.Outcome == ImportRowOutcome.New), r => Assert.NotNull(r.PurchaseDate));
    }

    [GeneratedRegex(@"^\|\s*(?<label>[^|]+?)\s*\|\s*(?<count>\d+)\s*\|$")]
    private static partial Regex TableRow();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx"))) return dir.FullName;

        throw new InvalidOperationException("Could not find the repository root (Giim.slnx).");
    }
}
