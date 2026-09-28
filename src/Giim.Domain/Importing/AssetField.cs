namespace Giim.Domain.Importing;

/// <summary>The register fields a spreadsheet column can be mapped to.</summary>
public enum AssetField
{
    SerialNumber,
    AssetTag,
    Manufacturer,
    Model,
    Category,
    AssignedTo,
    Department,
    Location,
    PurchaseDate,
    WarrantyExpiry,
    Status,
    Supplier,
    Cost,
    Notes,
}

/// <summary>Maps each register field to the spreadsheet header it comes from.</summary>
public sealed class ColumnMapping
{
    private static readonly Dictionary<AssetField, string[]> Synonyms = new()
    {
        [AssetField.SerialNumber]   = ["serial no.", "serial no", "serial number", "serial", "s/n", "sn", "serialnumber"],
        [AssetField.AssetTag]       = ["asset tag", "asset no.", "asset number", "tag", "asset id", "assettag"],
        [AssetField.Manufacturer]   = ["make", "manufacturer", "vendor", "brand"],
        [AssetField.Model]          = ["model", "product", "model name"],
        [AssetField.Category]       = ["type", "category", "device type", "asset type", "product type"],
        [AssetField.AssignedTo]     = ["assigned to", "user", "owner", "assigned user", "staff member"],
        [AssetField.Department]     = ["department", "dept", "business unit", "team"],
        [AssetField.Location]       = ["location", "site", "office"],
        [AssetField.PurchaseDate]   = ["purchase date", "purchased", "date purchased", "acquired"],
        [AssetField.WarrantyExpiry] = ["warranty end", "warranty expiry", "warranty", "warranty expires"],
        [AssetField.Status]         = ["status", "state", "asset state"],
        [AssetField.Supplier]       = ["supplier", "reseller"],
        [AssetField.Cost]           = ["cost", "price", "purchase price"],
        [AssetField.Notes]          = ["notes", "comments", "comment"],
    };

    public Dictionary<AssetField, string> Columns { get; init; } = [];

    public string? HeaderFor(AssetField field) => Columns.GetValueOrDefault(field);

    /// <summary>Best guess from the header names. The user confirms or corrects it before import.</summary>
    public static ColumnMapping Suggest(IEnumerable<string> headers)
    {
        var mapping = new ColumnMapping();
        var available = headers.ToList();

        foreach (var (field, names) in Synonyms)
        {
            var match = available.FirstOrDefault(h => names.Contains(h.Trim().ToLowerInvariant()));
            if (match is null) continue;

            mapping.Columns[field] = match;
            available.Remove(match);
        }

        return mapping;
    }
}
