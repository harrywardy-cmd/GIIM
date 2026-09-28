using Giim.Domain.Common;

namespace Giim.Domain.Cases;

public enum TaskKind { Manual, Automated }

public enum TaskState { Pending, Running, Waiting, Done, Failed, Skipped }

public sealed class ChecklistTask : Entity
{
    public Guid CaseId { get; set; }
    public int Order { get; set; }
    public required string Title { get; set; }
    public TaskKind Kind { get; set; }
    public TaskState Status { get; set; } = TaskState.Pending;

    /// <summary>Destructive automations (disable account, wipe device) need a named approver.</summary>
    public bool RequiresApproval { get; set; }
    public string? ApprovedBy { get; set; }

    /// <summary>What generated this task: a profile item (onboarding) or an assignment (offboarding).</summary>
    public Guid? SourceId { get; set; }
    public string? ServiceDeskTaskId { get; set; }

    public string? AssignedTo { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
}
