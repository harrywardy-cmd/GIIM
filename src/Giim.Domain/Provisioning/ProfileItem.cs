using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Provisioning;

public enum ProfileItemType { Application, Hardware, OktaGroup, LicenceGroup, ManualTask }

public sealed class ProfileItem : Entity
{
    public Guid RoleProfileId { get; set; }
    public ProfileItemType Type { get; set; }
    public required string Description { get; set; }

    public Guid? ApplicationId { get; set; }
    public AssetCategory? HardwareCategory { get; set; }

    /// <summary>Group name for OktaGroup / LicenceGroup items.</summary>
    public string? GroupName { get; set; }
}
