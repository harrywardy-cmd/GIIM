using Giim.Domain.Assets;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Provisioning;

namespace Giim.Domain.Tests;

public class ServiceCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(10));
    private static readonly Person Leaver = new() { EmployeeId = "E1", DisplayName = "Lee Leaver", DepartmentId = Guid.NewGuid() };

    private static ServiceCase LeaverCase() => ChecklistGenerator.ForOffboarding(Leaver, [], [], "req-1", new DateOnly(2026, 10, 9), "tina");

    private static ChecklistTask Disable(ServiceCase c) => c.Tasks.Single(t => t.Title.StartsWith("Disable AD account", StringComparison.Ordinal));

    [Fact]
    public void New_case_is_open_with_its_last_day_and_ticket()
    {
        var c = LeaverCase();

        Assert.Equal(CaseStatus.Open, c.Status);
        Assert.Equal(new DateOnly(2026, 10, 9), c.DueDate);
        Assert.Equal("REQ-1", c.ServiceDeskRequestId);
        Assert.Equal("tina", c.CreatedBy);
    }

    [Fact]
    public void Completing_a_task_records_who_when_and_why_and_starts_the_case()
    {
        var c = LeaverCase();
        var task = c.Tasks.First(t => !t.RequiresApproval);

        c.CompleteTask(task.Id, "tina", "Done in Entra", Now);

        Assert.Equal(TaskState.Done, task.Status);
        Assert.Equal("tina", task.CompletedBy);
        Assert.Equal(Now, task.CompletedAt);
        Assert.Contains("Done in Entra", task.Notes, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.InProgress, c.Status);
    }

    [Fact]
    public void Destructive_steps_need_an_administrator_then_someone_else_to_do_them()
    {
        var c = LeaverCase();
        var disable = Disable(c);

        var notYet = Assert.Throws<DomainException>(() => c.CompleteTask(disable.Id, "tina", null, Now));
        Assert.Contains("approval", notYet.Message, StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => c.ApproveTask(disable.Id, "tina", isAdministrator: false, Now));

        c.ApproveTask(disable.Id, "ada.admin", isAdministrator: true, Now);
        Assert.Equal("ada.admin", disable.ApprovedBy);
        var sameHands = Assert.Throws<DomainException>(() => c.CompleteTask(disable.Id, "ADA.ADMIN", null, Now));
        Assert.Contains("someone else", sameHands.Message, StringComparison.Ordinal);

        c.CompleteTask(disable.Id, "tina", null, Now);
        Assert.Equal(TaskState.Done, disable.Status);
    }

    [Fact]
    public void Skipping_needs_a_reason()
    {
        var c = LeaverCase();
        var task = c.Tasks[0];

        Assert.Throws<DomainException>(() => c.SkipTask(task.Id, "tina", " ", Now));
        c.SkipTask(task.Id, "tina", "Contractor: no mailbox", Now);

        Assert.Equal(TaskState.Skipped, task.Status);
    }

    [Fact]
    public void Case_completes_when_every_task_is_finished_and_reopens_with_a_task()
    {
        var c = LeaverCase();
        foreach (var task in c.Tasks.Where(t => t.RequiresApproval)) c.ApproveTask(task.Id, "ada.admin", true, Now);
        foreach (var task in c.Tasks) c.CompleteTask(task.Id, "tina", null, Now);

        Assert.Equal(CaseStatus.Completed, c.Status);
        Assert.Equal(Now, c.CompletedAt);

        c.ReopenTask(c.Tasks[0].Id, "tina", Now.AddHours(1));

        Assert.Equal(CaseStatus.InProgress, c.Status);
        Assert.Null(c.CompletedAt);
        Assert.Equal(TaskState.Pending, c.Tasks[0].Status);
        Assert.Null(c.Tasks[0].CompletedBy);
    }

    [Fact]
    public void Finished_tasks_cannot_be_completed_again()
    {
        var c = LeaverCase();
        var task = c.Tasks.First(t => !t.RequiresApproval);
        c.CompleteTask(task.Id, "tina", null, Now);

        Assert.Throws<DomainException>(() => c.CompleteTask(task.Id, "tina", null, Now));
    }

    [Fact]
    public void Extra_tasks_can_be_added_and_go_to_the_end()
    {
        var c = LeaverCase();

        var added = c.AddTask("Collect building pass", "tina", Now);

        Assert.Equal(c.Tasks.Max(t => t.Order), added.Order);
        Assert.Equal(TaskKind.Manual, added.Kind);
    }

    [Fact]
    public void Cancelled_case_needs_a_reason_and_then_nothing_changes()
    {
        var c = LeaverCase();

        Assert.Throws<DomainException>(() => c.Cancel("tina", "", Now));
        c.Cancel("tina", "Resignation withdrawn", Now);

        Assert.Equal(CaseStatus.Cancelled, c.Status);
        Assert.Equal("Resignation withdrawn", c.CancellationReason);
        Assert.Throws<DomainException>(() => c.CompleteTask(c.Tasks[0].Id, "tina", null, Now));
        Assert.Throws<DomainException>(() => c.AddTask("x", "tina", Now));
    }

    [Fact]
    public void Starter_hardware_task_waits_on_its_device_request()
    {
        var laptopCategory = Guid.NewGuid();
        var profile = new RoleProfile { Name = "Sales" };
        profile.Items.Add(new ProfileItem { Type = ProfileItemType.Hardware, Description = "Standard laptop", CategoryId = laptopCategory });
        var starter = new Person { EmployeeId = "E2", DisplayName = "Sam Starter", DepartmentId = Guid.NewGuid(), StartDate = new DateOnly(2026, 10, 12) };
        var c = ChecklistGenerator.ForOnboarding(starter, profile, null, "tina");
        var hardware = c.Tasks.Single(t => t.Source == TaskSource.ProfileItem);

        Assert.Equal(laptopCategory, hardware.CategoryId);
        Assert.Equal(profile.Id, c.RoleProfileId);

        var request = Guid.NewGuid();
        c.LinkDeviceRequest(hardware.Id, request, "REQ1050", "tina", Now);

        Assert.Equal(TaskState.Waiting, hardware.Status);
        Assert.Equal(request, hardware.DeviceRequestId);
        Assert.Equal(CaseStatus.InProgress, c.Status);
        Assert.Throws<DomainException>(() => c.LinkDeviceRequest(hardware.Id, Guid.NewGuid(), "REQ1051", "tina", Now));
    }

    [Fact]
    public void Leaver_asset_tasks_point_at_the_asset()
    {
        var laptop = new Asset { SerialNumber = "5CD1", Manufacturer = "HP", Model = "840", Category = new AssetCategory { Name = "Laptop" } };

        var c = ChecklistGenerator.ForOffboarding(Leaver, [laptop], [], null);

        var recover = c.Tasks.Single(t => t.Source == TaskSource.Asset);
        Assert.Equal(laptop.Id, recover.SourceId);
    }
}
