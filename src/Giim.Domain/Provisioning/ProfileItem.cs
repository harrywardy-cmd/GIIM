using Giim.Domain.Common;

namespace Giim.Domain.Provisioning;

public enum ProfileItemType { Application, Hardware, StockItem, SecurityGroup, LicenceGroup, ManualTask }

public sealed class ProfileItem : Entity
{
    public Guid RoleProfileId { get; set; }
    public ProfileItemType Type { get; set; }
    public required string Description { get; set; }

    public Guid? ApplicationId { get; set; }

    /// <summary>Asset category for Hardware items (e.g. Laptop).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Stock item for StockItem items (e.g. Laptop bag).</summary>
    public Guid? StockItemId { get; set; }

    /// <summary>Group name for SecurityGroup / LicenceGroup items (an AD group, or a cloud-only Entra group).</summary>
    public string? GroupName { get; set; }

    /// <summary>The group is cloud-only (made in Entra ID, not synced from AD): GIIM adds people through Graph, not the agent.</summary>
    public bool CloudGroup { get; set; }
}
