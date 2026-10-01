using Giim.Domain.Common;
using Giim.Domain.Notifications;
using Giim.Domain.People;
using Giim.Domain.Requests;

namespace Giim.Domain.Tests;

public class DeviceRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(10));

    private static readonly Person Manager = new() { EmployeeId = "E1", DisplayName = "Emily Carter", DepartmentId = Guid.NewGuid() };
    private static readonly Person Recipient = new()
    {
        EmployeeId = "E2", DisplayName = "Tom Harris", DepartmentId = Manager.DepartmentId, ManagerId = Manager.Id, Status = PersonStatus.Active,
    };

    private static readonly RequestActor Technician = new("tech@co", "Tina Tech", "tech@co", null, false, true, false);
    private static readonly RequestActor Admin = new("admin@co", "Ada Admin", "admin@co", null, true, false, false);
    private static readonly RequestActor Approver = new("emily@co", "Emily Carter", "emily@co", Manager.Id, false, false, true);
    private static readonly RequestActor OtherManager = new("other@co", "Omar Other", "other@co", Guid.NewGuid(), false, false, true);
    private static readonly RequestActor Viewer = new("viewer@co", "Vera Viewer", null, null, false, false, false);

    private static NewDeviceRequest Laptop(Guid? approver = null) => new(
        Recipient.Id, Guid.NewGuid(), "Dell Latitude 7455", RequestReason.NewStarter, "New starter in Sales",
        RequestPriority.High, "16GB RAM, 512GB SSD", ApproverPersonId: approver);

    private static DeviceRequest Submitted(RequestActor? by = null) =>
        DeviceRequest.Submit(1028, Laptop(), Recipient, Manager.Id, by ?? Technician, Now).Request;

    private static DeviceRequest Approved()
    {
        var request = Submitted();
        request.Approve(Approver, Now, "OK", "SALES-2026");
        return request;
    }

    [Fact]
    public void Submitted_request_waits_for_the_recipients_manager()
    {
        var (request, events) = DeviceRequest.Submit(1028, Laptop(), Recipient, Manager.Id, Technician, Now);

        Assert.Equal("REQ1028", request.Reference);
        Assert.Equal(RequestStatus.PendingApproval, request.Status);
        Assert.Equal(Manager.Id, request.ApproverPersonId);
        Assert.Equal(Recipient.DepartmentId, request.DepartmentId);
        Assert.Equal("tech@co", request.RequestedBy);
        Assert.Equal(RequestEventType.Submitted, Assert.Single(events).Type);
    }

    [Fact]
    public void A_manager_raising_a_request_for_their_own_team_member_approves_it_by_doing_so()
    {
        var (request, events) = DeviceRequest.Submit(1028, Laptop() with { BudgetCode = "SALES-2026" }, Recipient, Manager.Id, Approver, Now);

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Equal([RequestEventType.Submitted, RequestEventType.Approved], events.Select(e => e.Type));
        Assert.Equal("Raised by the approver", request.DecisionComment);
        Assert.Equal("SALES-2026", request.BudgetCode);
    }

    [Theory]
    [InlineData("", "New starter")]
    [InlineData("Dell Latitude", " ")]
    public void Device_and_reason_are_required(string device, string reason)
    {
        var details = Laptop() with { DeviceDescription = device, Reason = reason };

        Assert.Throws<DomainException>(() => DeviceRequest.Submit(1, details, Recipient, Manager.Id, Technician, Now));
    }

    [Fact]
    public void Viewers_cannot_raise_requests()
    {
        Assert.Throws<DomainException>(() => DeviceRequest.Submit(1, Laptop(), Recipient, Manager.Id, Viewer, Now));
    }

    [Fact]
    public void Nobody_approves_a_device_for_themselves()
    {
        var error = Assert.Throws<DomainException>(() => DeviceRequest.Submit(1, Laptop(), Recipient, Recipient.Id, Technician, Now));
        Assert.Contains("can't approve a device for themselves", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Devices_are_not_requested_for_people_who_have_left()
    {
        var leaver = new Person { EmployeeId = "E9", DisplayName = "Gone Person", DepartmentId = Guid.NewGuid(), Status = PersonStatus.Left };

        Assert.Throws<DomainException>(() => DeviceRequest.Submit(1, Laptop(), leaver, Manager.Id, Technician, Now));
    }

    [Fact]
    public void The_named_approver_approves_with_comment_and_budget_code()
    {
        var request = Submitted();

        var approved = request.Approve(Approver, Now.AddHours(2), "Approved for new starter.", "SALES-2026");

        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.Equal("emily@co", request.DecidedBy);
        Assert.Equal("Emily Carter", request.DecidedByName);
        Assert.Equal(Now.AddHours(2), request.DecidedAt);
        Assert.Equal("SALES-2026", request.BudgetCode);
        Assert.Equal(RequestStatus.PendingApproval, approved.FromStatus);
        Assert.Equal(RequestStatus.Approved, approved.ToStatus);
    }

    [Fact]
    public void Another_manager_cannot_approve()
    {
        var request = Submitted();

        Assert.False(request.CanDecide(OtherManager));
        var error = Assert.Throws<DomainException>(() => request.Approve(OtherManager, Now, null, null));
        Assert.Contains("Only the approver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Technicians_cannot_approve()
    {
        Assert.False(Submitted().CanDecide(Technician));
    }

    [Fact]
    public void An_administrator_can_approve_any_request_but_not_one_they_raised()
    {
        Assert.True(Submitted().CanDecide(Admin));

        var ownRequest = Submitted(by: Admin);
        Assert.False(ownRequest.CanDecide(Admin));
        var error = Assert.Throws<DomainException>(() => ownRequest.Approve(Admin, Now, null, null));
        Assert.Contains("someone else must approve", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_manager_on_record_only_an_administrator_can_approve()
    {
        var request = DeviceRequest.Submit(1, Laptop(), Recipient, approverPersonId: null, Technician, Now).Request;

        Assert.False(request.CanDecide(Approver));
        Assert.True(request.CanDecide(Admin));
    }

    [Fact]
    public void Rejection_needs_a_reason_and_the_request_stays_on_record()
    {
        var request = Submitted();

        Assert.Throws<DomainException>(() => request.Reject(Approver, Now, "  "));
        request.Reject(Approver, Now, "Existing laptop available in inventory.");

        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal("Existing laptop available in inventory.", request.DecisionComment);
        Assert.True(request.IsClosed);
        Assert.Throws<DomainException>(() => request.Approve(Approver, Now, null, null));
    }

    [Fact]
    public void Asking_for_more_information_goes_back_to_the_requester_then_to_the_approver()
    {
        var request = Submitted();

        request.RequestInfo(Approver, Now, "Is there a spare in stock?");
        Assert.Equal(RequestStatus.InfoRequested, request.Status);
        Assert.False(request.CanDecide(Approver));
        Assert.Throws<DomainException>(() => request.Answer(OtherManager, Now, "Not mine to answer"));

        var answered = request.Answer(Technician, Now, "No spares until next month.");

        Assert.Equal(RequestStatus.PendingApproval, request.Status);
        Assert.Equal("No spares until next month.", answered.Comment);
        Assert.True(request.CanDecide(Approver));
    }

    [Fact]
    public void Answering_restarts_the_approvers_reminder_clock()
    {
        var request = Submitted();
        request.RequestInfo(Approver, Now.AddDays(1), "Is there a spare in stock?");

        request.Answer(Technician, Now.AddDays(5), "No spares until next month.");

        // Reminders wait a full interval after this, rather than counting from the original submission.
        Assert.Equal(Now.AddDays(5), request.LastApprovalReminderAt);
    }

    [Fact]
    public void Ordered_then_received_then_handed_over()
    {
        var request = Approved();
        var assetId = Guid.NewGuid();

        request.Order(Technician, Now, new PurchaseOrderDetails("Dell", "po33445", 1849m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)));
        Assert.Equal(RequestStatus.Ordered, request.Status);
        Assert.Equal("PO33445", request.PurchaseOrder);

        request.Receive(Technician, Now, assetId, "Dell Latitude 7455 (AT20001)");
        Assert.Equal(RequestStatus.Received, request.Status);
        Assert.Equal(assetId, request.AssetId);

        Assert.Throws<DomainException>(() => request.Complete(Technician, Now, Guid.NewGuid(), "another laptop"));
        request.Complete(Technician, Now, assetId, "Dell Latitude 7455 (AT20001)");

        Assert.Equal(RequestStatus.Completed, request.Status);
        Assert.NotNull(request.CompletedAt);
    }

    [Fact]
    public void Approved_request_can_be_met_from_existing_stock()
    {
        var request = Approved();

        request.Complete(Technician, Now, Guid.NewGuid(), "Spare laptop");

        Assert.Equal(RequestStatus.Completed, request.Status);
    }

    [Fact]
    public void Only_it_orders_receives_and_hands_over()
    {
        var request = Approved();

        Assert.False(request.CanOrder(Approver));
        Assert.False(request.CanFulfil(Approver));
        var error = Assert.Throws<DomainException>(() =>
            request.Order(Approver, Now, new PurchaseOrderDetails("Dell", "PO1", null, new DateOnly(2026, 10, 1))));
        Assert.Contains("Only IT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_ordered_before_approval()
    {
        var request = Submitted();

        var error = Assert.Throws<DomainException>(() =>
            request.Order(Technician, Now, new PurchaseOrderDetails("Dell", "PO1", null, new DateOnly(2026, 10, 1))));
        Assert.Contains("only be ordered when approved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Order_needs_supplier_and_po_and_sensible_dates()
    {
        var request = Approved();

        Assert.Throws<DomainException>(() => request.Order(Technician, Now, new PurchaseOrderDetails("", "PO1", null, new DateOnly(2026, 10, 1))));
        Assert.Throws<DomainException>(() => request.Order(Technician, Now, new PurchaseOrderDetails("Dell", " ", null, new DateOnly(2026, 10, 1))));
        Assert.Throws<DomainException>(() => request.Order(Technician, Now,
            new PurchaseOrderDetails("Dell", "PO1", null, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 1))));
    }

    [Fact]
    public void The_requester_or_it_can_cancel_with_a_reason_until_received()
    {
        var request = Submitted(by: Approver with { PersonId = Guid.NewGuid() });   // raised by a manager who isn't the approver

        Assert.False(request.CanCancel(OtherManager));
        Assert.Throws<DomainException>(() => request.Cancel(Approver with { PersonId = Guid.NewGuid() }, Now, ""));
        request.Cancel(Approver with { PersonId = Guid.NewGuid() }, Now, "Starter withdrew");

        Assert.Equal(RequestStatus.Cancelled, request.Status);
        Assert.Equal("Starter withdrew", request.CancellationReason);
        Assert.Throws<DomainException>(() => request.Cancel(Technician, Now, "again"));
    }

    [Fact]
    public void Comments_are_recorded_without_changing_the_status()
    {
        var request = Submitted();

        var comment = request.Comment(OtherManager, Now, "Please prioritise");

        Assert.Equal(RequestStatus.PendingApproval, request.Status);
        Assert.Equal(RequestEventType.Commented, comment.Type);
        Assert.Throws<DomainException>(() => request.Comment(Viewer, Now, "hello"));
    }

    [Fact]
    public void Notification_retries_with_growing_gaps_then_gives_up()
    {
        var mail = Notification.Create("Test", "emily@co", "Emily", "Subject", "text", "<p>html</p>", null, Now);

        mail.MarkFailed("smtp down", Now);
        Assert.Equal(Now.AddMinutes(1), mail.NextAttemptAt);
        mail.MarkFailed("smtp down", Now);
        Assert.Equal(Now.AddMinutes(2), mail.NextAttemptAt);
        for (var i = 2; i < Notification.MaxAttempts; i++) mail.MarkFailed("smtp down", Now);

        Assert.Equal(NotificationStatus.Failed, mail.Status);
        Assert.Throws<DomainException>(() => Notification.Create("Test", "not-an-address", null, "s", "t", "h", null, Now));
    }
}
