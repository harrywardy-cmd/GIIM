using Giim.Domain.Common;
using Giim.Domain.People;

namespace Giim.Domain.Requests;

public enum RequestStatus
{
    PendingApproval,
    InfoRequested,   // the approver asked a question; back to PendingApproval when answered
    Approved,
    Rejected,
    Cancelled,
    Ordered,
    Received,
    Completed,
}

public enum RequestPriority { Low, Medium, High, Urgent }

/// <summary>Why the device is needed; the free-text reason adds the detail.</summary>
public enum RequestReason { NewStarter, Replacement, Additional, Upgrade, Other }

/// <summary>What someone asks for when they raise a request.</summary>
public sealed record NewDeviceRequest(
    Guid RecipientPersonId,
    Guid CategoryId,
    string DeviceDescription,
    RequestReason ReasonType,
    string Reason,
    RequestPriority Priority = RequestPriority.Medium,
    string? Specifications = null,
    string? Notes = null,
    DateOnly? NeededBy = null,
    decimal? EstimatedCost = null,
    string? BudgetCode = null,
    string? TicketNumber = null,
    Guid? ApproverPersonId = null);

/// <summary>Purchasing details recorded when an approved request is ordered.</summary>
public sealed record PurchaseOrderDetails(
    string Supplier,
    string PurchaseOrder,
    decimal? Cost,
    DateOnly OrderedOn,
    DateOnly? ExpectedDelivery = null,
    string? TrackingNumber = null,
    string? Notes = null);

/// <summary>
/// A request for a device, from submission to the device being handed over (brief §4-9). Requests are never deleted:
/// rejected and cancelled ones stay for history and reporting. Every change returns a <see cref="DeviceRequestEvent"/>
/// for the request's permanent history.
/// </summary>
public sealed class DeviceRequest : Entity
{
    /// <summary>Sequential number shown as REQ1001, REQ1002...</summary>
    public int Number { get; private init; }
    public string Reference => FormatReference(Number);

    public required string RequestedBy { get; init; }
    public required string RequestedByName { get; init; }
    public string? RequestedByEmail { get; init; }

    public Guid RecipientPersonId { get; private init; }
    /// <summary>The recipient's department when the request was raised.</summary>
    public Guid DepartmentId { get; private init; }
    /// <summary>Who approves: the recipient's manager unless the requester chose someone else. Null: an administrator.</summary>
    public Guid? ApproverPersonId { get; private init; }

    public Guid CategoryId { get; private init; }
    public required string DeviceDescription { get; init; }
    public string? Specifications { get; private init; }
    public RequestReason ReasonType { get; private init; }
    public required string Reason { get; init; }
    public RequestPriority Priority { get; private init; }
    public DateOnly? NeededBy { get; private init; }
    public decimal? EstimatedCost { get; private init; }
    public string? TicketNumber { get; private init; }
    public string? Notes { get; private init; }

    public RequestStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private init; }

    public string? DecidedBy { get; private set; }
    public string? DecidedByName { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    /// <summary>Approval comment, or the reason for rejection.</summary>
    public string? DecisionComment { get; private set; }
    public string? BudgetCode { get; private set; }

    public string? Supplier { get; private set; }
    public string? PurchaseOrder { get; private set; }
    public decimal? OrderCost { get; private set; }
    public DateOnly? OrderedOn { get; private set; }
    public DateOnly? ExpectedDelivery { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? PurchaseNotes { get; private set; }

    /// <summary>The device that fulfils the request: created on receipt, or chosen from stock.</summary>
    public Guid? AssetId { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>The last status reported to the linked ServiceDesk Plus ticket, so each change is noted there once.</summary>
    public RequestStatus? ServiceDeskNotifiedStatus { get; set; }

    public bool IsClosed => Status is RequestStatus.Rejected or RequestStatus.Cancelled or RequestStatus.Completed;

    public static string FormatReference(int number) => $"REQ{number}";

    /// <summary>
    /// Raises a request. If the person raising it is also its approver (a manager asking for their own team
    /// member), it is approved straight away and the history says so; nobody else can approve their own request.
    /// </summary>
    public static (DeviceRequest Request, IReadOnlyList<DeviceRequestEvent> Events) Submit(int number, NewDeviceRequest details,
        Person recipient, Guid? approverPersonId, RequestActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.CanRaise)
            throw new DomainException("Only managers, technicians and administrators can raise device requests.");
        if (recipient.Status == PersonStatus.Left)
            throw new DomainException($"{recipient.DisplayName} has left the organisation.");
        if (string.IsNullOrWhiteSpace(details.DeviceDescription))
            throw new DomainException("Say which device is needed.");
        if (string.IsNullOrWhiteSpace(details.Reason))
            throw new DomainException("Give a reason for the request.");
        if (details.EstimatedCost < 0)
            throw new DomainException("Estimated cost can't be negative.");
        if (approverPersonId is { } approver && approver == recipient.Id)
            throw new DomainException($"{recipient.DisplayName} can't approve a device for themselves; choose another approver.");

        var request = new DeviceRequest
        {
            Number = number,
            RequestedBy = actor.Login,
            RequestedByName = actor.DisplayName,
            RequestedByEmail = actor.Email,
            RecipientPersonId = recipient.Id,
            DepartmentId = recipient.DepartmentId,
            ApproverPersonId = approverPersonId,
            CategoryId = details.CategoryId,
            DeviceDescription = details.DeviceDescription.Trim(),
            Specifications = Clean(details.Specifications),
            ReasonType = details.ReasonType,
            Reason = details.Reason.Trim(),
            Priority = details.Priority,
            NeededBy = details.NeededBy,
            EstimatedCost = details.EstimatedCost,
            BudgetCode = Clean(details.BudgetCode),
            TicketNumber = Clean(details.TicketNumber)?.ToUpperInvariant(),
            Notes = Clean(details.Notes),
            Status = RequestStatus.PendingApproval,
            SubmittedAt = now,
            CreatedAt = now,
        };

        var submitted = request.Record(actor, now, RequestEventType.Submitted, RequestStatus.PendingApproval, RequestStatus.PendingApproval,
            $"Requested {request.DeviceDescription} for {recipient.DisplayName}", null);

        if (approverPersonId is not null && actor.PersonId == approverPersonId)
            return (request, [submitted, request.Approve(actor, now, "Raised by the approver", details.BudgetCode, raisedByApprover: true)]);

        return (request, [submitted]);
    }

    // ---- Who may do what -------------------------------------------------------------------------------------------

    /// <summary>The approver named on the request, or any administrator; never the person who raised it.</summary>
    public bool CanDecide(RequestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Status == RequestStatus.PendingApproval && !IsRequester(actor)
            && (actor.IsAdministrator || (ApproverPersonId is { } approver && actor.PersonId == approver));
    }

    public bool CanAnswer(RequestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Status == RequestStatus.InfoRequested && (IsRequester(actor) || actor.CanProcess);
    }

    public bool CanCancel(RequestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Status is RequestStatus.PendingApproval or RequestStatus.InfoRequested or RequestStatus.Approved or RequestStatus.Ordered
            && (IsRequester(actor) || actor.CanProcess);
    }

    public bool CanOrder(RequestActor actor) => Status == RequestStatus.Approved && Processor(actor);
    public bool CanReceive(RequestActor actor) => Status == RequestStatus.Ordered && Processor(actor);
    public bool CanFulfil(RequestActor actor) => Status is RequestStatus.Approved or RequestStatus.Received && Processor(actor);

    private static bool Processor(RequestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.CanProcess;
    }

    private bool IsRequester(RequestActor actor) => string.Equals(actor.Login, RequestedBy, StringComparison.OrdinalIgnoreCase);

    // ---- Approval ----------------------------------------------------------------------------------------------------

    public DeviceRequestEvent Approve(RequestActor actor, DateTimeOffset now, string? comment, string? budgetCode) =>
        Approve(actor, now, comment, budgetCode, raisedByApprover: false);

    private DeviceRequestEvent Approve(RequestActor actor, DateTimeOffset now, string? comment, string? budgetCode, bool raisedByApprover)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!raisedByApprover) EnsureCanDecide(actor);
        Decide(actor, now, Clean(comment));
        if (Clean(budgetCode) is { } code) BudgetCode = code;
        return Move(actor, now, RequestEventType.Approved, RequestStatus.Approved, "Approved", DecisionComment);
    }

    /// <summary>A rejected request stays on record with the reason (brief §7).</summary>
    public DeviceRequestEvent Reject(RequestActor actor, DateTimeOffset now, string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureCanDecide(actor);
        var why = Clean(reason) ?? throw new DomainException("Give a reason for rejecting the request.");
        Decide(actor, now, why);
        return Move(actor, now, RequestEventType.Rejected, RequestStatus.Rejected, "Rejected", why);
    }

    public DeviceRequestEvent RequestInfo(RequestActor actor, DateTimeOffset now, string question)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureCanDecide(actor);
        var asked = Clean(question) ?? throw new DomainException("Say what information is needed.");
        return Move(actor, now, RequestEventType.InfoRequested, RequestStatus.InfoRequested, "More information requested", asked);
    }

    public DeviceRequestEvent Answer(RequestActor actor, DateTimeOffset now, string answer)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!CanAnswer(actor))
            throw Status == RequestStatus.InfoRequested
                ? new DomainException("Only the person who raised the request, or IT, can answer.")
                : new DomainException($"{Reference} isn't waiting for more information.");
        var given = Clean(answer) ?? throw new DomainException("Write the information the approver asked for.");
        return Move(actor, now, RequestEventType.InfoProvided, RequestStatus.PendingApproval, "Information provided", given);
    }

    public DeviceRequestEvent Cancel(RequestActor actor, DateTimeOffset now, string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!CanCancel(actor))
            throw IsClosed || Status == RequestStatus.Received
                ? new DomainException($"{Reference} is {StatusText(Status).ToLowerInvariant()} and can't be cancelled.")
                : new DomainException("Only the person who raised the request, or IT, can cancel it.");
        CancellationReason = Clean(reason) ?? throw new DomainException("Give a reason for cancelling.");
        return Move(actor, now, RequestEventType.Cancelled, RequestStatus.Cancelled, "Cancelled", CancellationReason);
    }

    // ---- Purchasing, receiving and handover ----------------------------------------------------------------------------

    public DeviceRequestEvent Order(RequestActor actor, DateTimeOffset now, PurchaseOrderDetails order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!CanOrder(actor))
            throw NotNow(actor, "ordered", "approved");
        Supplier = Clean(order.Supplier) ?? throw new DomainException("Enter the supplier.");
        PurchaseOrder = Clean(order.PurchaseOrder)?.ToUpperInvariant() ?? throw new DomainException("Enter the purchase order number.");
        if (order.Cost < 0) throw new DomainException("Cost can't be negative.");
        if (order.ExpectedDelivery < order.OrderedOn) throw new DomainException("Expected delivery can't be before the order date.");
        OrderCost = order.Cost;
        OrderedOn = order.OrderedOn;
        ExpectedDelivery = order.ExpectedDelivery;
        TrackingNumber = Clean(order.TrackingNumber);
        PurchaseNotes = Clean(order.Notes);
        return Move(actor, now, RequestEventType.Ordered, RequestStatus.Ordered, $"Ordered from {Supplier}, {PurchaseOrder}", PurchaseNotes);
    }

    /// <summary>The ordered device has arrived and its asset record has been created.</summary>
    public DeviceRequestEvent Receive(RequestActor actor, DateTimeOffset now, Guid assetId, string assetName)
    {
        if (!CanReceive(actor))
            throw NotNow(actor, "received", "ordered");
        AssetId = assetId;
        ReceivedAt = now;
        return Move(actor, now, RequestEventType.Received, RequestStatus.Received, $"Received: {assetName}", null);
    }

    /// <summary>The device has been assigned to the recipient: from stock (approved) or the one received (received).</summary>
    public DeviceRequestEvent Complete(RequestActor actor, DateTimeOffset now, Guid assetId, string assetName)
    {
        if (!CanFulfil(actor))
            throw NotNow(actor, "handed over", "approved or received");
        if (Status == RequestStatus.Received && AssetId is { } received && received != assetId)
            throw new DomainException($"{Reference} was received as a different device; hand over that one.");
        AssetId = assetId;
        CompletedAt = now;
        return Move(actor, now, RequestEventType.Completed, RequestStatus.Completed, $"Handed over: {assetName}", null);
    }

    /// <summary>A comment on the history without changing anything.</summary>
    public DeviceRequestEvent Comment(RequestActor actor, DateTimeOffset now, string text)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.CanRaise) throw new DomainException("Only managers, technicians and administrators can comment.");
        var comment = Clean(text) ?? throw new DomainException("Write a comment.");
        return Record(actor, now, RequestEventType.Commented, Status, Status, "Comment", comment);
    }

    public static string StatusText(RequestStatus status) => status switch
    {
        RequestStatus.PendingApproval => "Pending approval",
        RequestStatus.InfoRequested => "More info needed",
        _ => status.ToString(),
    };

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private void EnsureCanDecide(RequestActor actor)
    {
        if (CanDecide(actor)) return;
        if (Status != RequestStatus.PendingApproval)
            throw new DomainException($"{Reference} is {StatusText(Status).ToLowerInvariant()}, not waiting for approval.");
        if (IsRequester(actor))
            throw new DomainException("You raised this request, so someone else must approve it.");
        throw new DomainException("Only the approver named on the request, or an administrator, can decide it.");
    }

    private DomainException NotNow(RequestActor actor, string action, string needed)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.CanProcess
            ? new DomainException($"{Reference} is {StatusText(Status).ToLowerInvariant()}; it can only be {action} when {needed}.")
            : new DomainException("Only IT (technicians and administrators) can do that.");
    }

    private void Decide(RequestActor actor, DateTimeOffset now, string? comment)
    {
        DecidedBy = actor.Login;
        DecidedByName = actor.DisplayName;
        DecidedAt = now;
        DecisionComment = comment;
    }

    private DeviceRequestEvent Move(RequestActor actor, DateTimeOffset now, RequestEventType type, RequestStatus to, string summary, string? comment)
    {
        var from = Status;
        Status = to;
        UpdatedAt = now;
        return Record(actor, now, type, from, to, summary, comment);
    }

    private DeviceRequestEvent Record(RequestActor actor, DateTimeOffset now, RequestEventType type, RequestStatus from, RequestStatus to,
        string summary, string? comment) => new()
    {
        RequestId = Id,
        OccurredAt = now,
        Type = type,
        FromStatus = from,
        ToStatus = to,
        Actor = actor.Login,
        ActorName = actor.DisplayName,
        Summary = summary,
        Comment = comment,
    };

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
