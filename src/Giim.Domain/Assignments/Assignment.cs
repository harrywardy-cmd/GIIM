using Giim.Domain.Common;

namespace Giim.Domain.Assignments;

/// <summary>
/// Records that a person holds an asset or an app. Offboarding is generated from these,
/// because they reflect what someone actually has, not what their department template says.
/// </summary>
public sealed class Assignment : Entity
{
    public Guid PersonId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? ApplicationId { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }

    public Guid? CaseId { get; set; }
    public string? ServiceDeskRequestId { get; set; }

    /// <summary>Technician who issued the asset.</summary>
    public string? AssignedBy { get; set; }
    public string? Notes { get; set; }

    public bool IsActive => EndedAt is null;

    public static Assignment ForAsset(Guid personId, Guid assetId, string? requestId = null) =>
        new() { PersonId = personId, AssetId = assetId, ServiceDeskRequestId = requestId };

    public static Assignment ForApplication(Guid personId, Guid applicationId, string? requestId = null) =>
        new() { PersonId = personId, ApplicationId = applicationId, ServiceDeskRequestId = requestId };
}
