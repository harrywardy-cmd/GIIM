using System.Text.Json;
using Giim.Domain.Auditing;
using Giim.Domain.Importing;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Importing;

public sealed record AssetImportResult(
    string SheetName,
    IReadOnlyList<string> Headers,
    ColumnMapping Mapping,
    ImportSummary Summary,
    IReadOnlyList<AnalyzedRow> Rows);

/// <summary>
/// Preview and commit for legacy register imports. Both run the same analysis, and commit
/// only ever inserts new serials; existing assets are never overwritten by a spreadsheet.
/// </summary>
public sealed class AssetImportService(GiimDbContext db)
{
    public async Task<AssetImportResult> PreviewAsync(Stream xlsx, ColumnMapping? mapping, CancellationToken cancellationToken)
    {
        var sheet = ExcelSheetReader.Read(xlsx);
        mapping ??= ColumnMapping.Suggest(sheet.Headers);

        var unknown = mapping.Columns.Values.Where(h => !sheet.Headers.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"Mapped column(s) not found in the sheet: {string.Join(", ", unknown)}");

        var existingSerials = (await db.Assets.Select(a => a.SerialNumber).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var existingTags = (await db.Assets.Where(a => a.AssetTag != null).Select(a => a.AssetTag!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var categories = await db.AssetCategories.Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var rows = AssetImportAnalyzer.Analyze(sheet.Rows, mapping, existingSerials, existingTags, categories);
        return new AssetImportResult(sheet.SheetName, sheet.Headers, mapping, AssetImportAnalyzer.Summarize(rows), rows);
    }

    public async Task<AssetImportResult> CommitAsync(Stream xlsx, ColumnMapping mapping, string fileName, string actor, CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(xlsx, mapping, cancellationToken);
        var newAssets = preview.Rows.Where(r => r.Outcome == ImportRowOutcome.New).Select(r => r.ToAsset()).ToList();

        // One SaveChanges = one transaction: the assets and the audit entry are saved together or not at all.
        db.Assets.AddRange(newAssets);
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = actor,
            Action = "AssetImport",
            EntityType = "Asset",
            AfterJson = JsonSerializer.Serialize(new
            {
                fileName,
                preview.SheetName,
                mapping = preview.Mapping.Columns,
                preview.Summary.TotalRows,
                imported = newAssets.Count,
                preview.Summary.AlreadyInRegister,
                preview.Summary.Rejected,
            }),
        });

        await db.SaveChangesAsync(cancellationToken);
        return preview;
    }
}
