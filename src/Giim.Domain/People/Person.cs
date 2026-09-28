using Giim.Domain.Common;

namespace Giim.Domain.People;

public enum PersonStatus { Pending, Active, Leaving, Left }

/// <summary>Full = laptop, mailbox and checklist. Light = bulk/frontline, mostly automated.</summary>
public enum ProvisioningTrack { Full, Light }

public sealed class Person : Entity
{
    public required string EmployeeId { get; set; }
    public required string DisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Email { get; set; }
    public string? JobTitle { get; set; }
    public string? Location { get; set; }

    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? ManagerId { get; set; }

    public ProvisioningTrack Track { get; set; } = ProvisioningTrack.Full;
    public PersonStatus Status { get; set; } = PersonStatus.Pending;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    // Identity keys in each connected system, filled in as provisioning completes.
    public Guid? AdObjectGuid { get; set; }
    public string? OktaUserId { get; set; }
    public Guid? EntraObjectId { get; set; }

    /// <summary>When the directory sync last saw this person; null if they were added by hand.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
}
