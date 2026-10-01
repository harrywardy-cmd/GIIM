using System.Net;
using Giim.Connectors.ServiceDesk;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.Integration;
using Giim.Domain.People;
using Giim.Domain.Requests;
using Giim.Infrastructure.Cases;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.ServiceDesk;

/// <summary>
/// Keeps GIIM and ServiceDesk Plus in step:
/// 1. tickets ServiceDesk Plus told GIIM about become starter or leaver checklists (read from the ticket itself);
/// 2. changes to checklists and device requests linked to a ticket are queued as notes on that ticket;
/// 3. queued notes are sent, with retries.
/// </summary>
public sealed partial class ServiceDeskSync(
    GiimDbContext db,
    IServiceDeskClient client,
    CaseService cases,
    IOptions<ServiceDeskOptions> options,
    IOptions<GiimOptions> giim,
    TimeProvider clock,
    ILogger<ServiceDeskSync> logger)
{
    public const string Actor = "ServiceDesk Plus";

    // ServiceDesk Plus acts as a technician when it raises a starter's device requests (their manager still approves).
    private static readonly RequestActor RequestActor = new("servicedesk", Actor, null, null, false, true, false);

    private ServiceDeskOptions Options => options.Value;
    private string? BaseUrl => string.IsNullOrWhiteSpace(giim.Value.PublicBaseUrl) ? null : giim.Value.PublicBaseUrl.TrimEnd('/');

    // ---- 1. Tickets in ------------------------------------------------------------------------------------------------

    public async Task<int> ProcessInboxAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.ServiceDeskInbound.Where(e => e.Status == InboundStatus.Pending && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt).Take(batchSize).Select(e => e.Id).ToListAsync(cancellationToken);
        foreach (var id in due)
        {
            db.ChangeTracker.Clear();
            var inbound = await db.ServiceDeskInbound.FirstAsync(e => e.Id == id, cancellationToken);
            await ProcessAsync(inbound, cancellationToken);
        }
        return due.Count;
    }

    private async Task ProcessAsync(ServiceDeskInboundEvent inbound, CancellationToken cancellationToken)
    {
        ServiceDeskRequest? ticket;
        try
        {
            ticket = await client.GetRequestAsync(inbound.RequestKey, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            inbound.Retry($"Couldn't read the ticket from ServiceDesk Plus: {e.Message}", clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();
        if (ticket is null)
        {
            inbound.Ignore("ServiceDesk Plus has no ticket with that ID.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var kind = Classify(ticket.Template);
        inbound.Identify(ticket.DisplayId, kind);
        if (kind == TicketKind.Unknown)
        {
            inbound.Ignore($"Template “{ticket.Template}” isn’t set up as a starter or leaver template.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        if (await db.Cases.AnyAsync(c => c.ServiceDeskRequestKey == ticket.Key || c.ServiceDeskRequestId == ticket.DisplayId, cancellationToken))
        {
            inbound.Ignore("This ticket already has a checklist.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        ServiceCase created;
        try
        {
            created = kind == TicketKind.Starter
                ? await cases.StartOnboardingAsync(await StarterFromAsync(ticket, cancellationToken), Actor, cancellationToken)
                : await StartLeaverAsync(ticket, cancellationToken);
        }
        catch (DomainException e)
        {
            // Anything half-added (e.g. a new person) is dropped; only the outcome and a note to the ticket are saved.
            db.ChangeTracker.Clear();
            db.ServiceDeskInbound.Update(inbound);   // the whole record, including what was learned before the clear
            inbound.NeedsAttention(e.Message, clock.GetUtcNow());
            db.ServiceDeskUpdates.Add(ServiceDeskUpdate.Create(ticket.DisplayId, ticket.Key, UpdateKind.Note,
                $"<p>GIIM couldn’t create the {(kind == TicketKind.Starter ? "starter" : "leaver")} checklist automatically: " +
                $"{E(e.Message)}</p><p>IT will set it up in GIIM by hand.</p>", clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            LogNeedsAttention(logger, ticket.DisplayId, e.Message);
            return;
        }

        var raised = kind == TicketKind.Starter && Options.AutoRaiseDeviceRequests
            ? await RaiseDeviceRequestsAsync(created.Id, cancellationToken)
            : [];
        db.ChangeTracker.Clear();
        db.ServiceDeskInbound.Update(inbound);   // the whole record, including what was learned before the clear
        inbound.Processed(created.Id, $"{(kind == TicketKind.Starter ? "Starter" : "Leaver")} checklist created" +
            (raised.Count > 0 ? $"; device requests {string.Join(", ", raised)} raised." : "."), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        LogProcessed(logger, ticket.DisplayId, kind);
    }

    private TicketKind Classify(string? template) =>
        template is null ? TicketKind.Unknown
        : Options.Starter.Templates.Any(t => string.Equals(t, template, StringComparison.OrdinalIgnoreCase)) ? TicketKind.Starter
        : Options.Leaver.Templates.Any(t => string.Equals(t, template, StringComparison.OrdinalIgnoreCase)) ? TicketKind.Leaver
        : TicketKind.Unknown;

    /// <summary>A starter's details from the ticket's fields, matched to GIIM's people and departments. Never guesses.</summary>
    internal async Task<StarterDetails> StarterFromAsync(ServiceDeskRequest ticket, CancellationToken cancellationToken)
    {
        string? Field(string name) => Read(ticket, Options.Starter, name);
        var employeeId = Field("EmployeeId") ?? throw Missing("employee ID", Options.Starter, "EmployeeId");
        var startDate = ServiceDeskJson.ParseDate(Field("StartDate"), Zone) ?? throw Missing("start date", Options.Starter, "StartDate");
        var track = Enum.TryParse<ProvisioningTrack>(Field("Track"), ignoreCase: true, out var t) ? t : (ProvisioningTrack?)null;
        var notes = $"From ServiceDesk Plus ticket {ticket.DisplayId}: {ticket.Subject}";

        var existing = await db.People.AsNoTracking().Where(p => p.EmployeeId == employeeId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return new StarterDetails(existing, null, startDate, track, null, ticket.DisplayId, notes, ticket.Key);

        var name = Field("Name") ?? throw Missing("starter’s name", Options.Starter, "Name");
        var departmentText = Field("Department") ?? throw Missing("department", Options.Starter, "Department");
        var departmentId = await db.Departments.AsNoTracking()
            .Where(d => d.Code == departmentText || d.Name == departmentText).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException($"Department “{departmentText}” isn’t in GIIM.");
        var managerEmail = Field("ManagerEmail");
        var managerId = managerEmail is null ? (Guid?)null
            : await db.People.AsNoTracking().Where(p => p.UserPrincipalName == managerEmail || p.Email == managerEmail)
                .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken)
              ?? throw new DomainException($"The manager “{managerEmail}” isn’t in GIIM’s staff directory.");

        return new StarterDetails(null, new NewStarterPerson(employeeId, name, departmentId, Field("JobTitle"), managerId, null),
            startDate, track, null, ticket.DisplayId, notes, ticket.Key);
    }

    private async Task<ServiceCase> StartLeaverAsync(ServiceDeskRequest ticket, CancellationToken cancellationToken)
    {
        string? Field(string name) => Read(ticket, Options.Leaver, name);
        var employeeId = Field("EmployeeId");
        var email = Field("Email");
        if (employeeId is null && email is null) throw Missing("employee ID or email", Options.Leaver, "EmployeeId");
        var lastDay = ServiceDeskJson.ParseDate(Field("LastDay"), Zone) ?? throw Missing("last day", Options.Leaver, "LastDay");

        var personId = await db.People.AsNoTracking()
            .Where(p => (employeeId != null && p.EmployeeId == employeeId) || (email != null && (p.UserPrincipalName == email || p.Email == email)))
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException($"The leaver ({employeeId ?? email}) isn’t in GIIM’s staff directory.");
        return await cases.StartOffboardingAsync(personId, lastDay, ticket.DisplayId,
            $"From ServiceDesk Plus ticket {ticket.DisplayId}: {ticket.Subject}", Actor, ticket.Key, cancellationToken);
    }

    /// <summary>Raises a device request for each of a new starter's devices. A failure is noted, not fatal.</summary>
    private async Task<List<string>> RaiseDeviceRequestsAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var raised = new List<string>();
        var tasks = await db.ChecklistTasks.AsNoTracking()
            .Where(t => t.CaseId == caseId && t.CategoryId != null && t.DeviceRequestId == null).OrderBy(t => t.Order)
            .Select(t => t.Id).ToListAsync(cancellationToken);
        foreach (var taskId in tasks)
        {
            try
            {
                db.ChangeTracker.Clear();
                var request = await cases.RaiseDeviceRequestAsync(caseId, taskId, null, null, RequestActor, BaseUrl ?? "", cancellationToken);
                raised.Add(request.Reference);
            }
            catch (DomainException e)
            {
                LogDeviceRequestFailed(logger, caseId, e.Message);
            }
        }
        return raised;
    }

    // ---- 2. Changes out: queue notes ------------------------------------------------------------------------------------

    /// <summary>Queues a note for each checklist or device request whose status changed since the ticket was last told.</summary>
    public async Task<int> QueueStatusNotesAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var queued = 0;

        var changedCases = await db.Cases
            .Where(c => c.ServiceDeskRequestId != null && (c.ServiceDeskNotifiedStatus == null || c.ServiceDeskNotifiedStatus != c.Status))
            .Take(100).ToListAsync(cancellationToken);
        foreach (var c in changedCases)
        {
            var person = await db.People.AsNoTracking().Where(p => p.Id == c.PersonId).Select(p => p.DisplayName).FirstAsync(cancellationToken);
            var tasks = await db.ChecklistTasks.AsNoTracking().Where(t => t.CaseId == c.Id).ToListAsync(cancellationToken);
            var what = $"{(c.Type == CaseType.Onboarding ? "starter" : "leaver")} checklist for {E(person)}";
            var link = Link("case", c.Id);
            string? note = (c.ServiceDeskNotifiedStatus, c.Status) switch
            {
                (null, CaseStatus.Cancelled) => null,
                (null, _) => $"<p>GIIM created the {what}: {tasks.Count} tasks.{link}</p>",
                (_, CaseStatus.Completed) => $"<p>GIIM {what} is complete: {tasks.Count(t => t.Status == TaskState.Done)} done, " +
                    $"{tasks.Count(t => t.Status == TaskState.Skipped)} skipped.</p>{Skipped(tasks)}{link}",
                (_, CaseStatus.Cancelled) => $"<p>GIIM {what} was cancelled by {E(c.CancelledBy)}: {E(c.CancellationReason)}</p>",
                (CaseStatus.Completed, _) => $"<p>GIIM {what} was reopened.{link}</p>",
                _ => null,   // e.g. started: not worth a note
            };
            if (note is not null)
            {
                db.ServiceDeskUpdates.Add(ServiceDeskUpdate.Create(c.ServiceDeskRequestId!, c.ServiceDeskRequestKey, UpdateKind.Note, note, now, caseId: c.Id));
                queued++;
            }
            if (c.Status == CaseStatus.Completed && c.ServiceDeskNotifiedStatus != CaseStatus.Completed && Options.ResolveWhenComplete)
                db.ServiceDeskUpdates.Add(ServiceDeskUpdate.Create(c.ServiceDeskRequestId!, c.ServiceDeskRequestKey, UpdateKind.Resolve,
                    $"GIIM {(c.Type == CaseType.Onboarding ? "starter" : "leaver")} checklist for {person} completed.", now, caseId: c.Id));
            c.ServiceDeskNotifiedStatus = c.Status;
        }

        var changedRequests = await db.DeviceRequests
            .Where(r => r.TicketNumber != null && (r.ServiceDeskNotifiedStatus == null || r.ServiceDeskNotifiedStatus != r.Status))
            .Take(100).ToListAsync(cancellationToken);
        foreach (var r in changedRequests)
        {
            var person = await db.People.AsNoTracking().Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).FirstAsync(cancellationToken);
            var approver = await db.People.AsNoTracking().Where(p => p.Id == r.ApproverPersonId).Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken);
            var what = $"Device request {r.Reference} ({E(r.DeviceDescription)} for {E(person)})";
            var link = Link("request", r.Id);
            string? note = r.Status switch
            {
                RequestStatus.PendingApproval when r.ServiceDeskNotifiedStatus is null =>
                    $"<p>{what} raised in GIIM; waiting for {E(approver ?? "an administrator")} to approve.{link}</p>",
                RequestStatus.Approved when r.ServiceDeskNotifiedStatus is null => $"<p>{what} raised and approved in GIIM.{link}</p>",
                RequestStatus.Approved => $"<p>{what} approved by {E(r.DecidedByName)}.{link}</p>",
                RequestStatus.Rejected => $"<p>{what} rejected by {E(r.DecidedByName)}: {E(r.DecisionComment)}</p>",
                RequestStatus.Ordered => $"<p>{what} ordered from {E(r.Supplier)} ({E(r.PurchaseOrder)}).</p>",
                RequestStatus.Completed => $"<p>{what} handed over.{link}</p>",
                RequestStatus.Cancelled => $"<p>{what} cancelled: {E(r.CancellationReason)}</p>",
                _ => null,
            };
            if (note is not null)
            {
                var key = await db.Cases.AsNoTracking().Where(c => c.ServiceDeskRequestId == r.TicketNumber && c.ServiceDeskRequestKey != null)
                    .Select(c => c.ServiceDeskRequestKey).FirstOrDefaultAsync(cancellationToken);
                db.ServiceDeskUpdates.Add(ServiceDeskUpdate.Create(r.TicketNumber!, key, UpdateKind.Note, note, now, deviceRequestId: r.Id));
                queued++;
            }
            r.ServiceDeskNotifiedStatus = r.Status;
        }

        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    // ---- 3. Send queued notes --------------------------------------------------------------------------------------------

    public async Task<int> SendUpdatesAsync(int batchSize, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var due = await db.ServiceDeskUpdates.Where(u => u.Status == UpdateStatus.Pending && u.NextAttemptAt <= now)
            .OrderBy(u => u.NextAttemptAt).Take(batchSize).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var update in due)
        {
            try
            {
                update.RequestKey ??= await client.FindRequestKeyAsync(TicketDigits(update.DisplayId), cancellationToken);
                if (update.RequestKey is null)
                {
                    update.MarkFailed($"ServiceDesk Plus has no ticket {update.DisplayId}.", clock.GetUtcNow(), permanent: true);
                }
                else
                {
                    if (update.Kind == UpdateKind.Resolve) await client.ResolveAsync(update.RequestKey, update.Content, cancellationToken);
                    else await client.AddNoteAsync(update.RequestKey, update.Content, cancellationToken);
                    update.MarkSent(clock.GetUtcNow());
                    sent++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                update.MarkFailed(e.Message, clock.GetUtcNow());
                LogSendFailed(logger, update.DisplayId, update.Attempts, e);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(Options.TimeZone);

    private static string? Read(ServiceDeskRequest ticket, TicketMapping mapping, string name) =>
        mapping.Fields.TryGetValue(name, out var key) && ticket.Fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim() : null;

    private static DomainException Missing(string what, TicketMapping mapping, string name) =>
        new(mapping.Fields.TryGetValue(name, out var key)
            ? $"The ticket has no {what} (field {key})."
            : $"GIIM doesn’t know which ticket field holds the {what} (ServiceDesk:{name} isn’t mapped).");

    /// <summary>Ticket numbers are typed as "5501", "REQ5501" or "#5501"; ServiceDesk Plus knows them as 5501.</summary>
    public static string TicketDigits(string displayId)
    {
        var digits = new string([.. displayId.Where(char.IsAsciiDigit)]);
        return digits.Length > 0 ? digits : displayId;
    }

    private string Link(string kind, Guid id) =>
        BaseUrl is null ? "" : $" <a href=\"{E($"{BaseUrl}/?{kind}={id}")}\">Open in GIIM</a>";

    private static string Skipped(List<ChecklistTask> tasks)
    {
        var skipped = tasks.Where(t => t.Status == TaskState.Skipped).ToList();
        return skipped.Count == 0 ? ""
            : "<p>Skipped:</p><ul>" + string.Concat(skipped.Select(t => $"<li>{E(t.Title)}: {E(t.Notes?.Split('\n').LastOrDefault())}</li>")) + "</ul>";
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    [LoggerMessage(Level = LogLevel.Information, Message = "ServiceDesk Plus ticket {Ticket}: {Kind} checklist created.")]
    private static partial void LogProcessed(ILogger logger, string ticket, TicketKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ServiceDesk Plus ticket {Ticket} needs attention: {Reason}")]
    private static partial void LogNeedsAttention(ILogger logger, string ticket, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't raise a device request for checklist {CaseId}: {Reason}")]
    private static partial void LogDeviceRequestFailed(ILogger logger, Guid caseId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Note to ServiceDesk Plus ticket {Ticket} failed (attempt {Attempts}); will retry.")]
    private static partial void LogSendFailed(ILogger logger, string ticket, int attempts, Exception exception);
}
