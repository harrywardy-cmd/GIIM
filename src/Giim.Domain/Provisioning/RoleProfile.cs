using Giim.Domain.Common;
using Giim.Domain.People;

namespace Giim.Domain.Provisioning;

/// <summary>What a new starter in a department (and optionally a job role) should receive.</summary>
public sealed class RoleProfile : Entity
{
    public required string Name { get; set; }
    public Guid DepartmentId { get; set; }

    /// <summary>Null means the department's default profile.</summary>
    public string? JobTitle { get; set; }
    public ProvisioningTrack Track { get; set; } = ProvisioningTrack.Full;

    public List<ProfileItem> Items { get; init; } = [];
}
