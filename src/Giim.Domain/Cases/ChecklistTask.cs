using Giim.Domain.Common;

namespace Giim.Domain.Cases;

/// <summary>Manual: a person does it. Automated: a later phase will do it; until then a person ticks it off.</summary>
public enum TaskKind { Manual, Automated }

public enum TaskState { Pending, Running, Waiting, Done, Failed, Skipped }

/// <summary>What a task came from, so the checklist can link to it and tick itself off.</summary>
public enum TaskSource { None, ProfileItem, Asset, Application }

public sealed class ChecklistTask : Entity
{
    public Guid CaseId { get; set; }
    public int Order { get; set; }
    public required string Title { get; set; }
    public TaskKind Kind { get; set; }
    public TaskState Status { get; set; } = TaskState.Pending;

    /// <summary>Destructive steps (disable account, convert mailbox) need an administrator's approval first.</summary>
    public bool RequiresApproval { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>What generated this task: a profile item (starter), or an asset or app the leaver holds.</summary>
    public TaskSource Source { get; set; }
    public Guid? SourceId { get; set; }

    /// <summary>Hardware tasks: the kind of device, so a device request can be raised for it.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>The device request raised for this task; the task completes when the device is handed over.</summary>
    public Guid? DeviceRequestId { get; set; }

    public string? ServiceDeskTaskId { get; set; }

    public string? AssignedTo { get; set; }
    public string? CompletedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
}
