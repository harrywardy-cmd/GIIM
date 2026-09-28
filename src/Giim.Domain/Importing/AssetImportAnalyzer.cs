using Giim.Domain.Assets;

namespace Giim.Domain.Importing;

public enum ImportRowOutcome
{
    New,              // will be added to the register
    AlreadyInRegister, // serial already exists; left untouched
    Rejected,         // cannot be imported until the source is fixed
}

public enum ImportIssue
{
    BlankSerial,
    DuplicateSerialInFile,
    DuplicateAssetTag,
    MissingManufacturer,
    MissingModel,
    UnknownCategory,
    InvalidDate,
    InvalidCost,
    UnknownStatus,
    SerialCleaned,      // warning: spaces/case fixed
    ManufacturerRenamed, // warning: e.g. Hewlett-Packard -> HP
}

/// <summary>A cleaned row ready to become an <see cref="Asset"/>, or the reasons it can't.</summary>
public sealed record AnalyzedRow(
    int RowNumber,
    ImportRowOutcome Outcome,
    IReadOnlyList<ImportIssue> Issues,
    string? SerialNumber,
    string? AssetTag,
    string? Manufacturer,
    string? Model,
    string? CategoryName,
    Guid? CategoryId,
    AssetStatus? Status,
    string? AssignedTo,
    string? Location,
    DateOnly? PurchaseDate,
    DateOnly? WarrantyExpiry,
    string? Supplier,
    decimal? Cost,
    string? Notes)
{
    public Asset ToAsset()
    {
        if (Outcome != ImportRowOutcome.New)
            throw new InvalidOperationException($"Row {RowNumber} is {Outcome} and cannot be imported.");

        var asset = new Asset
        {
            SerialNumber = SerialNumber!,
            AssetTag = AssetTag,
            Manufacturer = Manufacturer!,
            Model = Model!,
            CategoryId = CategoryId!.Value,
            Location = Location,
            PurchaseDate = PurchaseDate,
            WarrantyExpiry = WarrantyExpiry,
            Supplier = Supplier,
            Cost = Cost,
            Notes = Notes,
            LegacyAssignedTo = AssignedTo,
        };
        asset.SetStatusFromMigration(Status ?? (AssignedTo is null ? AssetStatus.ReadyToDeploy : AssetStatus.Assigned));
        return asset;
    }
}

public sealed record ImportSummary(int TotalRows, int ToImport, int AlreadyInRegister, int Rejected, IReadOnlyDictionary<ImportIssue, int> IssueCounts);

/// <summary>
/// Checks every row of a legacy register before anything is saved. The same analysis drives
/// the preview the user reviews and the commit, so what they see is exactly what gets imported.
/// </summary>
public static class AssetImportAnalyzer
{
    private static readonly ImportIssue[] Warnings = [ImportIssue.SerialCleaned, ImportIssue.ManufacturerRenamed];

    /// <param name="categories">Active asset categories by name (case-insensitive).</param>
    public static IReadOnlyList<AnalyzedRow> Analyze(
        IEnumerable<IReadOnlyDictionary<string, string?>> rows,
        ColumnMapping mapping,
        ISet<string> existingSerials,
        ISet<string> existingAssetTags,
        IReadOnlyDictionary<string, Guid> categories)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(categories);

        var results = new List<AnalyzedRow>();
        var serialsSeen = new HashSet<string>(StringComparer.Ordinal);
        var tagsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1; // header row

        foreach (var row in rows)
        {
            rowNumber++;
            string? Get(AssetField field) =>
                mapping.HeaderFor(field) is { } header ? row.GetValueOrDefault(header) : null;

            var issues = new List<ImportIssue>();

            var rawSerial = Get(AssetField.SerialNumber);
            var serial = ImportNormalizer.Serial(rawSerial);
            if (serial is null) issues.Add(ImportIssue.BlankSerial);
            else if (serial != rawSerial) issues.Add(ImportIssue.SerialCleaned);

            var rawMake = ImportNormalizer.Text(Get(AssetField.Manufacturer));
            var make = ImportNormalizer.Manufacturer(rawMake);
            if (make is null) issues.Add(ImportIssue.MissingManufacturer);
            else if (make != rawMake) issues.Add(ImportIssue.ManufacturerRenamed);

            var model = ImportNormalizer.Text(Get(AssetField.Model));
            if (model is null) issues.Add(ImportIssue.MissingModel);

            var categoryName = ImportNormalizer.CategoryName(Get(AssetField.Category));
            Guid? categoryId = categoryName is not null && categories.TryGetValue(categoryName, out var id) ? id : null;
            if (categoryId is null) issues.Add(ImportIssue.UnknownCategory);

            var rawStatus = ImportNormalizer.Text(Get(AssetField.Status));
            var status = ImportNormalizer.Status(rawStatus);
            if (rawStatus is not null && status is null) issues.Add(ImportIssue.UnknownStatus);

            if (!ImportNormalizer.TryDate(Get(AssetField.PurchaseDate), out var purchased)) issues.Add(ImportIssue.InvalidDate);
            if (!ImportNormalizer.TryDate(Get(AssetField.WarrantyExpiry), out var warranty)) issues.Add(ImportIssue.InvalidDate);
            if (!ImportNormalizer.TryCost(Get(AssetField.Cost), out var cost)) issues.Add(ImportIssue.InvalidCost);

            var tag = ImportNormalizer.Text(Get(AssetField.AssetTag));

            var outcome = ImportRowOutcome.New;
            if (serial is not null && existingSerials.Contains(serial))
            {
                outcome = ImportRowOutcome.AlreadyInRegister;
            }
            else if (serial is not null && !serialsSeen.Add(serial))
            {
                issues.Add(ImportIssue.DuplicateSerialInFile);
            }
            else if (tag is not null && (existingAssetTags.Contains(tag) || !tagsSeen.Add(tag)))
            {
                issues.Add(ImportIssue.DuplicateAssetTag);
            }

            if (outcome == ImportRowOutcome.New && issues.Any(i => !Warnings.Contains(i)))
                outcome = ImportRowOutcome.Rejected;

            results.Add(new AnalyzedRow(
                rowNumber, outcome, issues, serial, tag, make, model, categoryName, categoryId, status,
                ImportNormalizer.Text(Get(AssetField.AssignedTo)),
                ImportNormalizer.Text(Get(AssetField.Location)),
                purchased, warranty,
                ImportNormalizer.Text(Get(AssetField.Supplier)),
                cost,
                ImportNormalizer.Text(Get(AssetField.Notes))));
        }

        return results;
    }

    public static ImportSummary Summarize(IReadOnlyCollection<AnalyzedRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new ImportSummary(
            TotalRows: rows.Count,
            ToImport: rows.Count(r => r.Outcome == ImportRowOutcome.New),
            AlreadyInRegister: rows.Count(r => r.Outcome == ImportRowOutcome.AlreadyInRegister),
            Rejected: rows.Count(r => r.Outcome == ImportRowOutcome.Rejected),
            IssueCounts: rows.SelectMany(r => r.Issues).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count()));
    }
}
