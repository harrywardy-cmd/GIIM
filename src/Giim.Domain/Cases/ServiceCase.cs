using Giim.Domain.Common;

namespace Giim.Domain.Cases;

public enum CaseType { Onboarding, Offboarding, Move, HardwareRequest, SoftwareRequest, Rma }

public enum CaseStatus { Open, InProgress, WaitingOnSync, Blocked, Completed, Cancelled }

/// <summary>
/// A starter or leaver checklist (and later other kinds of case), usually linked to one ServiceDesk Plus ticket.
/// Tasks are ticked off by people now; later phases automate some of them and tick them the same way. The case
/// completes itself when every task is done or skipped, and reopens if a task is reopened.
/// </summary>
public sealed class ServiceCase : Entity
{
    public CaseType Type { get; set; }
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public Guid PersonId { get; set; }

    public string? ServiceDeskRequestId { get; set; }

    /// <summary>The ticket’s internal ID in ServiceDesk Plus, when GIIM learned it from the ticket itself.</summary>
    public string? ServiceDeskRequestKey { get; set; }

    /// <summary>The last status reported to the ticket, so each change is noted there once.</summary>
    public CaseStatus? ServiceDeskNotifiedStatus { get; set; }

    /// <summary>Start date (starters) or last day (leavers).</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>The starter profile the checklist came from (onboarding).</summary>
    public Guid? RoleProfileId { get; set; }
    public string? CreatedBy { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancelledBy { get; private set; }

    public List<ChecklistTask> Tasks { get; init; } = [];

    public bool AllTasksFinished =>
        Tasks.Count > 0 && Tasks.All(t => t.Status is TaskState.Done or TaskState.Skipped);

    public bool IsClosed => Status is CaseStatus.Completed or CaseStatus.Cancelled;

    public ChecklistTask CompleteTask(Guid taskId, string actor, string? note, DateTimeOffset now)
    {
        var task = OpenTask(taskId);
        if (task.RequiresApproval && task.ApprovedBy is null)
            throw new DomainException($"“{task.Title}” needs an administrator’s approval first.");
        if (task.RequiresApproval && string.Equals(task.ApprovedBy, actor, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("You approved this step, so someone else must carry it out.");

        Finish(task, TaskState.Done, actor, Clean(note), now);
        return task;
    }

    public ChecklistTask SkipTask(Guid taskId, string actor, string reason, DateTimeOffset now)
    {
        var task = OpenTask(taskId);
        Finish(task, TaskState.Skipped, actor, Clean(reason) ?? throw new DomainException("Say why this task isn’t needed."), now);
        return task;
    }

    public ChecklistTask ReopenTask(Guid taskId, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var task = Find(taskId);
        if (task.Status is not (TaskState.Done or TaskState.Skipped))
            throw new DomainException("That task isn’t finished.");
        task.Status = TaskState.Pending;
        task.CompletedAt = null;
        task.CompletedBy = null;
        task.Notes = AppendNote(task.Notes, $"Reopened by {actor}");
        task.UpdatedAt = now;
        Refresh(now);
        return task;
    }

    /// <summary>
    /// Destructive steps (disabling an account, converting a mailbox) need an administrator to approve them, and then
    /// someone else to carry them out, so no single person can do it alone.
    /// </summary>
    public ChecklistTask ApproveTask(Guid taskId, string actor, bool isAdministrator, DateTimeOffset now)
    {
        var task = OpenTask(taskId);
        if (!task.RequiresApproval) throw new DomainException("That task doesn’t need approval.");
        if (!isAdministrator) throw new DomainException("Only an administrator can approve this step.");
        if (task.ApprovedBy is not null) throw new DomainException($"Already approved by {task.ApprovedBy}.");
        task.ApprovedBy = actor;
        task.ApprovedAt = now;
        task.UpdatedAt = now;
        UpdatedAt = now;
        return task;
    }

    public ChecklistTask AddTask(string title, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var task = new ChecklistTask
        {
            CaseId = Id,
            Order = Tasks.Count == 0 ? 1 : Tasks.Max(t => t.Order) + 1,
            Title = Clean(title) ?? throw new DomainException("Describe the task."),
            Kind = TaskKind.Manual,
            Notes = $"Added by {actor}",
            CreatedAt = now,
        };
        Tasks.Add(task);
        Refresh(now);
        return task;
    }

    public void Cancel(string actor, string reason, DateTimeOffset now)
    {
        if (IsClosed) throw new DomainException($"This case is already {Status.ToString().ToLowerInvariant()}.");
        CancellationReason = Clean(reason) ?? throw new DomainException("Say why the case is being cancelled.");
        CancelledBy = actor;
        Status = CaseStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>Links a hardware task to the device request raised for it.</summary>
    public void LinkDeviceRequest(Guid taskId, Guid requestId, string reference, string actor, DateTimeOffset now)
    {
        var task = OpenTask(taskId);
        if (task.DeviceRequestId is not null) throw new DomainException("A device request has already been raised for this task.");
        task.DeviceRequestId = requestId;
        task.Status = TaskState.Waiting;
        task.Notes = AppendNote(task.Notes, $"{reference} raised by {actor}");
        task.UpdatedAt = now;
        Refresh(now);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private ChecklistTask Find(Guid taskId) =>
        Tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new KeyNotFoundException("Task not found.");

    private ChecklistTask OpenTask(Guid taskId)
    {
        EnsureNotCancelled();
        var task = Find(taskId);
        if (task.Status is TaskState.Done or TaskState.Skipped)
            throw new DomainException($"“{task.Title}” is already {task.Status.ToString().ToLowerInvariant()}.");
        return task;
    }

    private void EnsureNotCancelled()
    {
        if (Status == CaseStatus.Cancelled) throw new DomainException("This case was cancelled.");
    }

    private void Finish(ChecklistTask task, TaskState state, string actor, string? note, DateTimeOffset now)
    {
        task.Status = state;
        task.CompletedBy = actor;
        task.CompletedAt = now;
        if (note is not null) task.Notes = AppendNote(task.Notes, note);
        task.UpdatedAt = now;
        Refresh(now);
    }

    private void Refresh(DateTimeOffset now)
    {
        UpdatedAt = now;
        if (AllTasksFinished)
        {
            Status = CaseStatus.Completed;
            CompletedAt ??= now;
            return;
        }
        CompletedAt = null;
        Status = Tasks.Any(t => t.Status is TaskState.Done or TaskState.Skipped or TaskState.Waiting) ? CaseStatus.InProgress : CaseStatus.Open;
    }

    private static string AppendNote(string? existing, string note) =>
        string.IsNullOrWhiteSpace(existing) ? note : $"{existing}\n{note}";

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
