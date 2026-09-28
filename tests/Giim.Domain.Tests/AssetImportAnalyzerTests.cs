using Giim.Domain.Assets;
using Giim.Domain.Importing;

namespace Giim.Domain.Tests;

public class AssetImportAnalyzerTests
{
    private static readonly string[] Headers = ["Asset Tag", "Serial No.", "Make", "Model", "Type", "Assigned To", "Status"];

    private static Dictionary<string, string?> Row(string? tag, string? serial, string? make = "HP", string? model = "EliteBook",
        string? type = "Laptop", string? owner = null, string? status = null) => new()
    {
        ["Asset Tag"] = tag, ["Serial No."] = serial, ["Make"] = make, ["Model"] = model,
        ["Type"] = type, ["Assigned To"] = owner, ["Status"] = status,
    };

    private static IReadOnlyList<AnalyzedRow> Analyze(IEnumerable<Dictionary<string, string?>> rows, string[]? existingSerials = null) =>
        AssetImportAnalyzer.Analyze(rows, ColumnMapping.Suggest(Headers),
            new HashSet<string>(existingSerials ?? []), new HashSet<string>());

    [Fact]
    public void Suggest_maps_common_header_names()
    {
        var mapping = ColumnMapping.Suggest(Headers);

        Assert.Equal("Serial No.", mapping.HeaderFor(AssetField.SerialNumber));
        Assert.Equal("Make", mapping.HeaderFor(AssetField.Manufacturer));
        Assert.Equal("Type", mapping.HeaderFor(AssetField.Category));
        Assert.Equal("Assigned To", mapping.HeaderFor(AssetField.AssignedTo));
    }

    [Fact]
    public void Clean_row_is_new()
    {
        var row = Assert.Single(Analyze([Row("AT1", "5CG001")]));

        Assert.Equal(ImportRowOutcome.New, row.Outcome);
        Assert.Empty(row.Issues);
        Assert.Equal(2, row.RowNumber); // row 1 is the header
    }

    [Fact]
    public void Blank_serial_is_rejected()
    {
        var row = Assert.Single(Analyze([Row("AT1", "  ")]));

        Assert.Equal(ImportRowOutcome.Rejected, row.Outcome);
        Assert.Contains(ImportIssue.BlankSerial, row.Issues);
    }

    [Fact]
    public void Second_copy_of_a_serial_is_rejected_even_when_formatted_differently()
    {
        var rows = Analyze([Row("AT1", "5CG001"), Row("AT2", " 5cg001 ")]);

        Assert.Equal(ImportRowOutcome.New, rows[0].Outcome);
        Assert.Equal(ImportRowOutcome.Rejected, rows[1].Outcome);
        Assert.Contains(ImportIssue.DuplicateSerialInFile, rows[1].Issues);
    }

    [Fact]
    public void Cleanups_are_warnings_not_rejections()
    {
        var row = Assert.Single(Analyze([Row("AT1", " 5cg001 ", make: "Hewlett-Packard")]));

        Assert.Equal(ImportRowOutcome.New, row.Outcome);
        Assert.Contains(ImportIssue.SerialCleaned, row.Issues);
        Assert.Contains(ImportIssue.ManufacturerRenamed, row.Issues);
        Assert.Equal("5CG001", row.SerialNumber);
        Assert.Equal("HP", row.Manufacturer);
    }

    [Fact]
    public void Serial_already_in_register_is_left_alone()
    {
        var row = Assert.Single(Analyze([Row("AT1", "5CG001")], existingSerials: ["5CG001"]));

        Assert.Equal(ImportRowOutcome.AlreadyInRegister, row.Outcome);
    }

    [Fact]
    public void Duplicate_asset_tag_is_rejected()
    {
        var rows = Analyze([Row("AT1", "5CG001"), Row("at1", "5CG002")]);

        Assert.Contains(ImportIssue.DuplicateAssetTag, rows[1].Issues);
        Assert.Equal(ImportRowOutcome.Rejected, rows[1].Outcome);
    }

    [Fact]
    public void Unknown_category_is_rejected()
    {
        var row = Assert.Single(Analyze([Row("AT1", "5CG001", type: "Gizmo")]));

        Assert.Contains(ImportIssue.UnknownCategory, row.Issues);
        Assert.Equal(ImportRowOutcome.Rejected, row.Outcome);
    }

    [Fact]
    public void Imported_status_comes_from_the_sheet_or_the_owner()
    {
        var rows = Analyze([
            Row("AT1", "S1", status: "Returned"),
            Row("AT2", "S2", owner: "Grace Brown"),
            Row("AT3", "S3"),
        ]);

        Assert.Equal(AssetStatus.Returned, rows[0].ToAsset().Status);
        Assert.Equal(AssetStatus.Assigned, rows[1].ToAsset().Status);
        Assert.Equal("Grace Brown", rows[1].ToAsset().LegacyAssignedTo);
        Assert.Equal(AssetStatus.InStock, rows[2].ToAsset().Status);
    }
}
