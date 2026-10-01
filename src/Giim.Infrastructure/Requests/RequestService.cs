using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Notifications;
using Giim.Domain.People;
using Giim.Domain.Requests;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Cases;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Requests;

/// <summary>Raised when the request changed after the user loaded it, so their action may no longer make sense.</summary>
public sealed class RequestChangedException(string message) : Exception(message);

/// <summary>Tabs on the Requests page.</summary>
public enum RequestView { All, AwaitingMe, Mine, Pending, Approved, Ordered, Received, Completed, Closed }

public sealed record RequestListItem(
    Guid Id, int Number, string DeviceDescription, string Category, Guid RecipientId, string Recipient, string? Department,
    string RequestedByName, string? Approver, RequestStatus Status, RequestPriority Priority, DateTimeOffset SubmittedAt,
    DateOnly? NeededBy, string? TicketNumber, string? PurchaseOrder);

public sealed record RequestList(int Total, IReadOnlyList<RequestListItem> Items, IReadOnlyDictionary<RequestView, int> Counts);

/// <summary>
/// Device requests from submission to handover. Every change is saved together with its history entry and the
/// emails it triggers, or not at all.
/// </summary>
public sealed class RequestService(GiimDbContext db, AssetLifecycleService lifecycle, AssignmentService assignments, TimeProvider clock)
{
    /// <summary>
    /// The signed-in user as a request actor, linked to their staff record: by Entra object ID when the directory
    /// sync has recorded it (permanent), otherwise by login or email.
    /// </summary>
    public async Task<RequestActor> ActorAsync(string login, string displayName, string? email, Guid? entraObjectId,
        bool isAdministrator, bool isTechnician, bool isManager, CancellationToken cancellationToken)
    {
        var personId = entraObjectId is { } oid
            ? await db.People.AsNoTracking().Where(p => p.EntraObjectId == oid).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken)
            : null;
        personId ??= await db.People.AsNoTracking()
            .Where(p => p.UserPrincipalName == login || p.Email == login
                || (email != null && (p.Email == email || p.UserPrincipalName == email)))
            .OrderBy(p => p.Status == PersonStatus.Left)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return new RequestActor(login, displayName, email, personId, isAdministrator, isTechnician, isManager);
    }

    // ---- Changes -------------------------------------------------------------------------------------------------------

    public Task<DeviceRequest> SubmitAsync(NewDeviceRequest details, RequestActor actor, string baseUrl, CancellationToken cancellationToken) =>
        SubmitAsync(details, actor, baseUrl, alongside: null, cancellationToken);

    /// <summary>As above, plus <paramref name="alongside"/>: another change (such as linking a starter's checklist task) in the same save.</summary>
    public async Task<DeviceRequest> SubmitAsync(NewDeviceRequest details, RequestActor actor, string baseUrl, Action<DeviceRequest>? alongside,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var recipient = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == details.RecipientPersonId, cancellationToken)
            ?? throw new DomainException("Choose who the device is for.");
        var category = await db.AssetCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == details.CategoryId, cancellationToken)
            ?? throw new DomainException("Choose the type of device.");
        if (!category.IsActive)
            throw new DomainException($"The {category.Name} category is no longer in use.");
        if (details.ApproverPersonId is { } chosen
            && !await db.People.AnyAsync(p => p.Id == chosen && p.Status != PersonStatus.Left, cancellationToken))
            throw new DomainException("That approver isn't in the staff directory.");

        var number = (await db.Database
            .SqlQueryRaw<int>($"SELECT NEXT VALUE FOR [{GiimDbContext.RequestNumberSequence}] AS [Value]")
            .ToListAsync(cancellationToken))[0];

        var (request, events) = DeviceRequest.Submit(number, details, recipient, details.ApproverPersonId ?? recipient.ManagerId,
            actor, clock.GetUtcNow());
        db.DeviceRequests.Add(request);
        db.DeviceRequestEvents.AddRange(events);
        await QueueEmailsAsync(request, events, actor, baseUrl, null, cancellationToken);
        alongside?.Invoke(request);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    /// <summary>Applies one change (approve, reject, ask, answer, cancel, order, comment) to a request.</summary>
    public async Task<DeviceRequestEvent> ActAsync(Guid requestId, RequestStatus? expectedStatus, RequestActor actor, string baseUrl,
        Func<DeviceRequest, DateTimeOffset, DeviceRequestEvent> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var request = await LoadAsync(requestId, expectedStatus, cancellationToken);
        var change = action(request, clock.GetUtcNow());
        db.DeviceRequestEvents.Add(change);
        await QueueEmailsAsync(request, [change], actor, baseUrl, null, cancellationToken);
        await SaveAsync(request, cancellationToken);
        return change;
    }

    /// <summary>The ordered device has arrived: creates its asset record (purchase details from the order) and marks the request received.</summary>
    public async Task<Asset> ReceiveAsync(Guid requestId, RequestStatus? expectedStatus, NewAsset details, bool readyToIssue,
        Guid? locationId, RequestActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(actor);
        var request = await LoadAsync(requestId, expectedStatus, cancellationToken);
        if (!request.CanReceive(actor))
            _ = request.Receive(actor, clock.GetUtcNow(), Guid.Empty, "");   // throws the reason it can't be received

        var now = clock.GetUtcNow();
        var withOrder = details with
        {
            CategoryId = details.CategoryId == Guid.Empty ? request.CategoryId : details.CategoryId,
            Supplier = details.Supplier ?? request.Supplier,
            PurchaseOrder = details.PurchaseOrder ?? request.PurchaseOrder,
            Cost = details.Cost ?? request.OrderCost,
            PurchaseDate = details.PurchaseDate ?? request.OrderedOn,
        };
        var context = new ActionContext(actor.Login, request.TicketNumber, $"Received for {request.Reference}", now);

        try
        {
            return await lifecycle.CreateAsync(withOrder, context, readyToIssue ? AssetStatus.ReadyToDeploy : AssetStatus.Received, locationId,
                asset =>
                {
                    db.DeviceRequestEvents.Add(request.Receive(actor, now, asset.Id, asset.DisplayName));
                    return Task.CompletedTask;
                }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RequestChangedException($"Someone else updated {request.Reference} at the same moment. Refresh and try again.");
        }
    }

    /// <summary>
    /// Hands the device to the recipient: assigns the asset (with any accessories) and completes the request in one
    /// transaction. From stock when approved, or the device received for this request.
    /// </summary>
    public async Task FulfilAsync(Guid requestId, RequestStatus? expectedStatus, Guid assetId, AssetStatus? expectedAssetStatus,
        Guid? locationId, IReadOnlyList<AccessoryRequest> accessories, string? ticketNumber, RequestActor actor, string baseUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var preview = await db.DeviceRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Request not found.");
        if (!preview.CanFulfil(actor))
            _ = preview.Complete(actor, clock.GetUtcNow(), assetId, "");   // throws the reason it can't be handed over

        var now = clock.GetUtcNow();
        var context = new ActionContext(actor.Login, string.IsNullOrWhiteSpace(ticketNumber) ? preview.TicketNumber : ticketNumber,
            $"Handed over for {preview.Reference}", now);

        await assignments.AssignAsync(assetId, preview.RecipientPersonId, expectedAssetStatus, context, locationId, accessories,
            async (asset, _) =>
            {
                // Loaded inside the assignment's transaction, so the check and the change can't be split by someone else.
                var request = await LoadAsync(requestId, expectedStatus ?? preview.Status, cancellationToken);
                var completed = request.Complete(actor, now, asset.Id, asset.DisplayName);
                db.DeviceRequestEvents.Add(completed);
                await QueueEmailsAsync(request, [completed], actor, baseUrl, asset.DisplayName, cancellationToken);
                // A starter checklist waiting on this device ticks its task off.
                await CaseService.CompleteLinkedTasksAsync(db, t => t.DeviceRequestId == request.Id, actor.Login,
                    $"Handed over {asset.DisplayName} ({request.Reference})", now, cancellationToken);
            }, cancellationToken);
    }

    // ---- Queries -------------------------------------------------------------------------------------------------------

    public async Task<RequestList> ListAsync(RequestView view, string? search, Guid? personId, RequestActor actor, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var all = db.DeviceRequests.AsNoTracking();
        if (personId is { } person) all = all.Where(r => r.RecipientPersonId == person);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var number = term.StartsWith("REQ", StringComparison.OrdinalIgnoreCase) && int.TryParse(term[3..], out var n) ? n
                : int.TryParse(term, out var m) ? m : -1;
            all = all.Where(r => r.Number == number || r.DeviceDescription.Contains(term) || r.RequestedByName.Contains(term)
                || (r.PurchaseOrder != null && r.PurchaseOrder.Contains(term)) || (r.TicketNumber != null && r.TicketNumber.Contains(term))
                || db.People.Any(p => p.Id == r.RecipientPersonId && p.DisplayName.Contains(term)));
        }

        var byStatus = await all.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int Count(params RequestStatus[] statuses) => byStatus.Where(s => statuses.Contains(s.Key)).Sum(s => s.Count);
        var counts = new Dictionary<RequestView, int>
        {
            [RequestView.All] = byStatus.Sum(s => s.Count),
            [RequestView.AwaitingMe] = await Filter(all, RequestView.AwaitingMe, actor).CountAsync(cancellationToken),
            [RequestView.Mine] = await Filter(all, RequestView.Mine, actor).CountAsync(cancellationToken),
            [RequestView.Pending] = Count(RequestStatus.PendingApproval, RequestStatus.InfoRequested),
            [RequestView.Approved] = Count(RequestStatus.Approved),
            [RequestView.Ordered] = Count(RequestStatus.Ordered),
            [RequestView.Received] = Count(RequestStatus.Received),
            [RequestView.Completed] = Count(RequestStatus.Completed),
            [RequestView.Closed] = Count(RequestStatus.Rejected, RequestStatus.Cancelled),
        };

        var filtered = Filter(all, view, actor);
        var items = await filtered
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
            .Select(r => new RequestListItem(
                r.Id, r.Number, r.DeviceDescription,
                db.AssetCategories.Where(c => c.Id == r.CategoryId).Select(c => c.Name).First(),
                r.RecipientPersonId,
                db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First(),
                db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                r.RequestedByName,
                db.People.Where(p => p.Id == r.ApproverPersonId).Select(p => p.DisplayName).FirstOrDefault(),
                r.Status, r.Priority, r.SubmittedAt, r.NeededBy, r.TicketNumber, r.PurchaseOrder))
            .ToListAsync(cancellationToken);

        return new RequestList(counts[view], items, counts);
    }

    /// <summary>How many requests are waiting for this person's decision (the badge on Approvals).</summary>
    public Task<int> AwaitingCountAsync(RequestActor actor, CancellationToken cancellationToken) =>
        Filter(db.DeviceRequests.AsNoTracking(), RequestView.AwaitingMe, actor).CountAsync(cancellationToken);

    public async Task<(DeviceRequest Request, IReadOnlyList<DeviceRequestEvent> History, IReadOnlyList<Notification> Emails)?> GetAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.DeviceRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return null;
        var history = await db.DeviceRequestEvents.AsNoTracking().Where(e => e.RequestId == requestId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var emails = await db.Notifications.AsNoTracking().Where(n => n.RequestId == requestId)
            .OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken);
        return (request, history, emails);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Requests waiting for this person to decide: they are the approver named on the request; administrators also see
    /// requests with no approver on record. Nobody decides their own. (The Approvals page and the bell both use this.)
    /// </summary>
    internal static IQueryable<DeviceRequest> AwaitingApproval(IQueryable<DeviceRequest> requests, RequestActor actor) =>
        requests.Where(r => r.Status == RequestStatus.PendingApproval && r.RequestedBy != actor.Login
            && ((actor.PersonId != null && r.ApproverPersonId == actor.PersonId) || (actor.IsAdministrator && r.ApproverPersonId == null)));

    private static IQueryable<DeviceRequest> Filter(IQueryable<DeviceRequest> requests, RequestView view, RequestActor actor) => view switch
    {
        RequestView.AwaitingMe => AwaitingApproval(requests, actor),
        RequestView.Mine => requests.Where(r => r.RequestedBy == actor.Login),
        RequestView.Pending => requests.Where(r => r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.InfoRequested),
        RequestView.Approved => requests.Where(r => r.Status == RequestStatus.Approved),
        RequestView.Ordered => requests.Where(r => r.Status == RequestStatus.Ordered),
        RequestView.Received => requests.Where(r => r.Status == RequestStatus.Received),
        RequestView.Completed => requests.Where(r => r.Status == RequestStatus.Completed),
        RequestView.Closed => requests.Where(r => r.Status == RequestStatus.Rejected || r.Status == RequestStatus.Cancelled),
        _ => requests,
    };

    private async Task<DeviceRequest> LoadAsync(Guid requestId, RequestStatus? expectedStatus, CancellationToken cancellationToken)
    {
        var request = await db.DeviceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Request not found.");
        if (expectedStatus is { } expected && request.Status != expected)
            throw new RequestChangedException(
                $"{request.Reference} is now {DeviceRequest.StatusText(request.Status).ToLowerInvariant()} (you saw {DeviceRequest.StatusText(expected).ToLowerInvariant()}). Refresh and try again.");
        return request;
    }

    private async Task SaveAsync(DeviceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RequestChangedException($"Someone else updated {request.Reference} at the same moment. Refresh and try again.");
        }
    }

    /// <summary>Queues the emails a change triggers. Nobody is emailed about their own action.</summary>
    private async Task QueueEmailsAsync(DeviceRequest request, IReadOnlyList<DeviceRequestEvent> changes, RequestActor actor, string baseUrl,
        string? assetName, CancellationToken cancellationToken)
    {
        var recipient = await db.People.AsNoTracking().Where(p => p.Id == request.RecipientPersonId)
            .Select(p => new { p.DisplayName, Department = p.Department!.Name }).FirstAsync(cancellationToken);
        var approver = request.ApproverPersonId is { } approverId
            ? await db.People.AsNoTracking().Where(p => p.Id == approverId)
                .Select(p => new { p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var requesterAddress = request.RequestedByEmail ?? (request.RequestedBy.Contains('@', StringComparison.Ordinal) ? request.RequestedBy : null);
        var facts = new RequestEmailFacts(request, recipient.DisplayName, recipient.Department,
            $"{baseUrl.TrimEnd('/')}/?request={request.Id}");
        var now = clock.GetUtcNow();

        bool NotSelf(string? address) => address is not null
            && !string.Equals(address, actor.Email, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(address, actor.Login, StringComparison.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            var email = change.Type switch
            {
                RequestEventType.Submitted when request.Status == RequestStatus.PendingApproval && NotSelf(approver?.Address) =>
                    RequestEmails.ApprovalNeeded(facts, approver!.Address!, approver.DisplayName, now),
                RequestEventType.InfoProvided when NotSelf(approver?.Address) =>
                    RequestEmails.InformationProvided(facts, approver!.Address!, approver.DisplayName, change.Comment ?? "", now),
                RequestEventType.InfoRequested when NotSelf(requesterAddress) =>
                    RequestEmails.InformationRequested(facts, requesterAddress!, change.Comment ?? "", change.ActorName, now),
                RequestEventType.Approved or RequestEventType.Rejected when NotSelf(requesterAddress) =>
                    RequestEmails.Decided(facts, requesterAddress!, now),
                RequestEventType.Completed when NotSelf(requesterAddress) =>
                    RequestEmails.HandedOver(facts, requesterAddress!, assetName ?? request.DeviceDescription, now),
                _ => null,
            };
            if (email is not null) db.Notifications.Add(email);
        }
    }
}
