namespace Giim.Domain.Reporting;

/// <summary>The dashboard's headline numbers at the end of a day, so the dashboard can show how they've moved.</summary>
public sealed class DashboardSnapshot
{
    public DateOnly Date { get; init; }
    public DateTimeOffset TakenAt { get; set; }
    public int InService { get; set; }
    public int Assigned { get; set; }
    public int Available { get; set; }
    public int InRepair { get; set; }
    public int Retired { get; set; }
    public int Disposed { get; set; }
    public int PendingApproval { get; set; }
}
