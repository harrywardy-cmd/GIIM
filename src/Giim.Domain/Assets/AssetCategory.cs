using Giim.Domain.Common;

namespace Giim.Domain.Assets;

/// <summary>
/// A kind of serialised asset (Laptop, Monitor, Monitor mount...). Editable by IT, so new kinds of kit
/// don't need a code change. Items without serial numbers are stock items instead, see <c>Giim.Domain.Stock</c>.
/// </summary>
public sealed class AssetCategory : Entity
{
    public required string Name { get; set; }

    /// <summary>Devices of this kind should appear in Intune; used by reconciliation.</summary>
    public bool IsIntuneManaged { get; set; }

    /// <summary>Added to the offboarding checklist when the holder leaves.</summary>
    public bool ReturnOnOffboarding { get; set; } = true;

    public bool IsActive { get; set; } = true;

    /// <summary>Starting categories. Ids are fixed so migrations and existing data can refer to them.</summary>
    public static IReadOnlyList<AssetCategory> Defaults { get; } =
    [
        Default("0c7f6a1e-0001-4000-8000-000000000001", "Laptop", intune: true),
        Default("0c7f6a1e-0001-4000-8000-000000000002", "Desktop", intune: true),
        Default("0c7f6a1e-0001-4000-8000-000000000003", "Monitor", intune: false),
        Default("0c7f6a1e-0001-4000-8000-000000000004", "Dock", intune: false),
        Default("0c7f6a1e-0001-4000-8000-000000000005", "Phone", intune: true),
        Default("0c7f6a1e-0001-4000-8000-000000000006", "Tablet", intune: true),
        Default("0c7f6a1e-0001-4000-8000-000000000007", "Peripheral", intune: false),
        Default("0c7f6a1e-0001-4000-8000-000000000008", "Other", intune: false),
        Default("0c7f6a1e-0001-4000-8000-000000000009", "Monitor mount", intune: false),
    ];

    private static AssetCategory Default(string id, string name, bool intune) => new()
    {
        Id = Guid.Parse(id),
        CreatedAt = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
        Name = name,
        IsIntuneManaged = intune,
    };
}
