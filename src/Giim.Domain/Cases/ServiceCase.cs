using Giim.Domain.Common;

namespace Giim.Domain.Cases;

public enum CaseType { Onboarding, Offboarding, Move, HardwareRequest, SoftwareRequest, Rma }

public enum CaseStatus { Open, InProgress, WaitingOnSync, Blocked, Completed, Cancelled }

/// <summary>A unit of work linked to one ServiceDesk Plus request.</summary>
public sealed class ServiceCase : Entity
{
    public CaseType Type { get; set; }
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public Guid PersonId { get; set; }

    public string? ServiceDeskRequestId { get; set; }
    public DateOnly? DueDate { get; set; }

    public List<ChecklistTask> Tasks { get; init; } = [];

    public bool AllTasksFinished =>
        Tasks.Count > 0 && Tasks.All(t => t.Status is TaskState.Done or TaskState.Skipped);
}
