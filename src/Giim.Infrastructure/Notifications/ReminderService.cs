using Giim.Domain.Assets;
using Giim.Domain.Cases;
using Giim.Domain.Notifications;
using Giim.Domain.Requests;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Notifications;

/// <summary>
/// Reminders and digests. Everything is queued in the email outbox (the workers send it, with retries), and each
/// record remembers what was sent, so nothing goes twice:
/// - managers: once when a starter's or leaver's checklist is created;
/// - approvers: while a device request waits (from 2 days, then every 2 days, at most 3);
/// - a leaver's manager: equipment still out after the last day (weekly, at most 3);
/// - the IT team: a daily digest (only when something needs attention) and a weekly warranty list.
/// </summary>
public sealed class ReminderService(GiimDbContext db, IOptions<ReminderOptions> options, IOptions<GiimOptions> giim, TimeProvider clock)
{
    public const string ItDigestJob = "it-digest";
    public const string WarrantyJob = "warranty-list";

    private ReminderOptions O => options.Value;
    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(O.TimeZone);
    private DateTime LocalNow => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;
    private DateOnly Today => DateOnly.FromDateTime(LocalNow);
    private string? Link(string kind, Guid id) => giim.Value.BaseUrl is { } b ? $"{b}/?{kind}={id}" : null;

    /// <summary>Everything that is due now. Called every few minutes by the workers; returns what it did, for the log.</summary>
    public async Task<IReadOnlyList<string>> RunDueAsync(CancellationToken cancellationToken)
    {
        var done = new List<string>();
        void Note(string what, int count) { if (count > 0) done.Add($"{what}: {count}"); }

        if (O.ManagerEmails) Note("manager emails", await QueueManagerEmailsAsync(cancellationToken));
        Note("approval reminders", await QueueApprovalRemindersAsync(cancellationToken));
        Note("return reminders", await QueueReturnRemindersAsync(cancellationToken));

        var runs = await db.ScheduledJobRuns.AsNoTracking().ToDictionaryAsync(r => r.Name, cancellationToken);
        if (ReminderSchedule.DailyDue(LocalNow, O.DigestTime, runs.GetValueOrDefault(ItDigestJob)?.LastRunOn))
            done.Add(await RunJobAsync(ItDigestJob, cancellationToken));
        if (ReminderSchedule.WeeklyDue(LocalNow, O.WarrantyDay, O.DigestTime, runs.GetValueOrDefault(WarrantyJob)?.LastRunOn))
            done.Add(await RunJobAsync(WarrantyJob, cancellationToken));
        return done;
    }

    /// <summary>Runs a scheduled job now (also used by "Send now") and records the run.</summary>
    public async Task<string> RunJobAsync(string job, CancellationToken cancellationToken)
    {
        var result = job switch
        {
            ItDigestJob => await QueueItDigestAsync(cancellationToken),
            WarrantyJob => await QueueWarrantyListAsync(cancellationToken),
            _ => throw new KeyNotFoundException($"There is no job called {job}."),
        };
        var run = await db.ScheduledJobRuns.FirstOrDefaultAsync(r => r.Name == job, cancellationToken);
        if (run is null) db.ScheduledJobRuns.Add(run = new ScheduledJobRun { Name = job });
        run.LastRunOn = Today;
        run.LastRunAt = clock.GetUtcNow();
        run.LastResult = result.Length > 500 ? result[..500] : result;
        await db.SaveChangesAsync(cancellationToken);
        return $"{job}: {result}";
    }

    // ---- Managers: once per new checklist ---------------------------------------------------------------------------

    public async Task<int> QueueManagerEmailsAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var cases = await db.Cases
            .Where(c => c.ManagerEmailedAt == null && (c.Type == CaseType.Onboarding || c.Type == CaseType.Offboarding)
                && c.Status != CaseStatus.Cancelled && c.Status != CaseStatus.Completed)
            .OrderBy(c => c.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var c in cases)
        {
            c.ManagerEmailedAt = now;   // even without a manager on record, so it isn't looked at again
            var person = await db.People.AsNoTracking().Where(p => p.Id == c.PersonId)
                .Select(p => new { p.DisplayName, p.JobTitle, Department = p.Department!.Name, p.ManagerId }).FirstAsync(cancellationToken);
            var manager = person.ManagerId is { } managerId
                ? await db.People.AsNoTracking().Where(p => p.Id == managerId)
                    .Select(p => new { p.Id, p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken)
                : null;
            if (manager?.Address is null) continue;

            var tasks = await db.ChecklistTasks.AsNoTracking().Where(t => t.CaseId == c.Id).OrderBy(t => t.Order).ToListAsync(cancellationToken);
            if (c.Type == CaseType.Onboarding)
            {
                var requestIds = tasks.Where(t => t.DeviceRequestId != null).Select(t => t.DeviceRequestId!.Value).ToList();
                var requests = await db.DeviceRequests.AsNoTracking().Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
                var devices = tasks.Where(t => t.CategoryId != null).Select(t =>
                {
                    var name = t.Title.Replace("Allocate and scan: ", "", StringComparison.Ordinal);
                    return t.DeviceRequestId is { } id && requests.TryGetValue(id, out var r)
                        ? $"{name}: {r.Reference}, {DeviceRequest.StatusText(r.Status).ToLowerInvariant()}" + (r.Status == RequestStatus.PendingApproval && r.ApproverPersonId == manager.Id ? " (needs your approval)" : "")
                        : name;
                }).ToList();
                var access = tasks.Where(t => t.Source == TaskSource.ProfileItem && t.CategoryId == null)
                    .Select(t => t.Title.Replace("Grant app: ", "", StringComparison.Ordinal).Replace("Add to group: ", "Group: ", StringComparison.Ordinal)
                        .Replace("Add to licence group: ", "Licence: ", StringComparison.Ordinal).Replace("Issue from stock: ", "", StringComparison.Ordinal))
                    .ToList();
                db.Notifications.Add(ReminderEmails.Compose("ManagerStarter", manager.Address, manager.DisplayName,
                    $"New starter: {person.DisplayName} starts {ReminderEmails.Date(c.DueDate)}",
                    $"{person.DisplayName}{(person.JobTitle is null ? "" : $" ({person.JobTitle})")} starts in {person.Department} on {ReminderEmails.Date(c.DueDate)}. IT is getting the following ready.",
                    [new EmailSection("Devices", devices), new EmailSection("Apps, access and other items", access)],
                    "Open the starter checklist", Link("case", c.Id), now));
            }
            else
            {
                var assets = tasks.Where(t => t.Source == TaskSource.Asset)
                    .Select(t => t.Title.StartsWith("Recover ", StringComparison.Ordinal) ? t.Title["Recover ".Length..] : t.Title).ToList();
                db.Notifications.Add(ReminderEmails.Compose("ManagerLeaver", manager.Address, manager.DisplayName,
                    $"Leaver: {person.DisplayName}, last day {ReminderEmails.Date(c.DueDate)}",
                    assets.Count > 0
                        ? $"{person.DisplayName}'s last day is {ReminderEmails.Date(c.DueDate)}. Please make sure these are returned to IT by then."
                        : $"{person.DisplayName}'s last day is {ReminderEmails.Date(c.DueDate)}. They have no equipment on record; IT will still remove their access.",
                    [new EmailSection("To return", assets)], "Open the leaver checklist", Link("case", c.Id), now));
            }
            sent++;
        }
        return await SaveAsync(sent, cancellationToken);
    }

    // ---- Approvers: while a request waits ----------------------------------------------------------------------------

    public async Task<int> QueueApprovalRemindersAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var waitingSince = now - O.ApprovalReminderAfter;
        var requests = await db.DeviceRequests
            .Where(r => r.Status == RequestStatus.PendingApproval && r.ApproverPersonId != null && r.ApprovalReminders < O.MaxApprovalReminders
                && r.SubmittedAt <= waitingSince)
            .OrderBy(r => r.SubmittedAt).Take(100).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var r in requests)
        {
            if (!ReminderSchedule.ReminderDue(now, r.SubmittedAt + O.ApprovalReminderAfter, r.LastApprovalReminderAt, r.ApprovalReminders,
                    O.ApprovalReminderAfter, O.MaxApprovalReminders))
                continue;
            var approver = await db.People.AsNoTracking().Where(p => p.Id == r.ApproverPersonId)
                .Select(p => new { p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken);
            if (approver?.Address is null) continue;
            var recipient = await db.People.AsNoTracking().Where(p => p.Id == r.RecipientPersonId)
                .Select(p => new { p.DisplayName, Department = p.Department!.Name }).FirstAsync(cancellationToken);

            var facts = new RequestEmailFacts(r, recipient.DisplayName, recipient.Department, Link("request", r.Id) ?? "GIIM");
            db.Notifications.Add(RequestEmails.ApprovalReminder(facts, approver.Address, approver.DisplayName, (int)(now - r.SubmittedAt).TotalDays, now));
            r.ApprovalReminders++;
            r.LastApprovalReminderAt = now;
            sent++;
        }
        return await SaveAsync(sent, cancellationToken);
    }

    // ---- A leaver's manager: equipment still out ----------------------------------------------------------------------

    public async Task<int> QueueReturnRemindersAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var today = Today;
        var cases = await db.Cases
            .Where(c => c.Type == CaseType.Offboarding && c.Status != CaseStatus.Cancelled && c.Status != CaseStatus.Completed
                && c.DueDate < today && c.ReturnReminders < O.MaxReturnReminders)
            .OrderBy(c => c.DueDate).Take(100).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var c in cases)
        {
            // First reminder the day after the last day, then weekly.
            var firstAt = new DateTimeOffset(c.DueDate!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), Zone.GetUtcOffset(LocalNow));
            if (!ReminderSchedule.ReminderDue(now, firstAt, c.LastReturnReminderAt, c.ReturnReminders, O.ReturnReminderEvery, O.MaxReturnReminders))
                continue;

            var outstanding = await OutstandingAssetsAsync(c.Id, c.PersonId, cancellationToken);
            if (outstanding.Count == 0) continue;
            var person = await db.People.AsNoTracking().Where(p => p.Id == c.PersonId).Select(p => new { p.DisplayName, p.ManagerId }).FirstAsync(cancellationToken);
            var manager = await db.People.AsNoTracking().Where(p => p.Id == person.ManagerId)
                .Select(p => new { p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken);
            if (manager?.Address is null) continue;

            db.Notifications.Add(ReminderEmails.Compose("ReturnReminder", manager.Address, manager.DisplayName,
                $"Equipment not returned: {person.DisplayName}",
                $"{person.DisplayName}'s last day was {ReminderEmails.Date(c.DueDate)}. These items haven't been returned to IT yet. Please arrange for them to come back.",
                [new EmailSection("Still out", outstanding)], "Open the leaver checklist", Link("case", c.Id), now));
            c.ReturnReminders++;
            c.LastReturnReminderAt = now;
            sent++;
        }
        return await SaveAsync(sent, cancellationToken);
    }

    // ---- IT team: daily digest and weekly warranty list ---------------------------------------------------------------

    public async Task<string> QueueItDigestAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        if (O.ItTeam.Count == 0) return "no IT team address set (Reminders:ItTeamAddresses)";
        var now = clock.GetUtcNow();
        var today = Today;
        var soon = today.AddDays(O.DueSoonDays);

        var open = await db.Cases.AsNoTracking()
            .Where(c => (c.Type == CaseType.Onboarding || c.Type == CaseType.Offboarding) && c.Status != CaseStatus.Cancelled && c.Status != CaseStatus.Completed
                && c.DueDate != null && c.DueDate <= soon)
            .Select(c => new
            {
                c.Id, c.Type, c.DueDate, c.PersonId,
                Person = db.People.Where(p => p.Id == c.PersonId).Select(p => p.DisplayName).First(),
                Total = c.Tasks.Count,
                Done = c.Tasks.Count(t => t.Status == TaskState.Done || t.Status == TaskState.Skipped),
            })
            .OrderBy(c => c.DueDate).ToListAsync(cancellationToken);
        static string Line(string person, DateOnly? due, int done, int total) => $"{person}, {ReminderEmails.Date(due)}: {done}/{total} tasks done";

        var unreturned = new List<string>();
        foreach (var c in open.Where(c => c.Type == CaseType.Offboarding && c.DueDate < today))
        {
            var items = await OutstandingAssetsAsync(c.Id, c.PersonId, cancellationToken);
            if (items.Count > 0) unreturned.Add($"{c.Person} (last day {ReminderEmails.Date(c.DueDate)}): {items.Count} item{(items.Count == 1 ? "" : "s")} still out");
        }

        var approvalCutoff = now - O.ApprovalReminderAfter;
        var stalled = now.AddDays(-5);
        var notHandedOver = now.AddDays(-3);
        var requests = await db.DeviceRequests.AsNoTracking()
            .Where(r => (r.Status == RequestStatus.PendingApproval && r.SubmittedAt <= approvalCutoff)
                || (r.Status == RequestStatus.Approved && r.DecidedAt <= stalled)
                || (r.Status == RequestStatus.Received && r.ReceivedAt <= notHandedOver))
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new
            {
                r.Number, r.DeviceDescription, r.Status, r.SubmittedAt, r.DecidedAt, r.ReceivedAt,
                Recipient = db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First(),
                Approver = db.People.Where(p => p.Id == r.ApproverPersonId).Select(p => p.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        int Days(DateTimeOffset? since) => since is null ? 0 : (int)(now - since.Value).TotalDays;
        var requestLines = requests.Select(r =>
            $"{DeviceRequest.FormatReference(r.Number)} {r.DeviceDescription} for {r.Recipient}: " + r.Status switch
            {
                RequestStatus.PendingApproval => $"waiting {Days(r.SubmittedAt)} days for {r.Approver ?? "an administrator"} to approve",
                RequestStatus.Approved => $"approved {Days(r.DecidedAt)} days ago, not yet ordered or handed over",
                _ => $"received {Days(r.ReceivedAt)} days ago, not yet handed over",
            }).ToList();

        var sections = new List<EmailSection>
        {
            new("Overdue checklists", [.. open.Where(c => c.DueDate < today && c.Type == CaseType.Onboarding).Select(c => "Starter " + Line(c.Person, c.DueDate, c.Done, c.Total))]),
            new("Equipment not returned", unreturned),
            new($"Starting in the next {O.DueSoonDays} days", [.. open.Where(c => c.Type == CaseType.Onboarding && c.DueDate >= today).Select(c => Line(c.Person, c.DueDate, c.Done, c.Total))]),
            new($"Leaving in the next {O.DueSoonDays} days", [.. open.Where(c => c.Type == CaseType.Offboarding && c.DueDate >= today).Select(c => Line(c.Person, c.DueDate, c.Done, c.Total))]),
            new("Device requests held up", requestLines),
        };
        var count = sections.Sum(s => s.Lines.Count);
        if (count == 0) return "nothing needed attention";

        foreach (var address in O.ItTeam)
            db.Notifications.Add(ReminderEmails.Compose("ItDigest", address, null,
                $"GIIM daily summary, {ReminderEmails.Date(today)}: {count} item{(count == 1 ? "" : "s")} need attention",
                "Here's what needs attention today.", sections, "Open GIIM", giim.Value.BaseUrl, now));
        await db.SaveChangesAsync(cancellationToken);
        return $"{count} items, sent to {O.ItTeam.Count} address{(O.ItTeam.Count == 1 ? "" : "es")}";
    }

    public async Task<string> QueueWarrantyListAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        if (O.ItTeam.Count == 0) return "no IT team address set (Reminders:ItTeamAddresses)";
        var today = Today;
        var until = today.AddDays(O.WarrantyDays);
        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.Status != AssetStatus.Retired && a.Status != AssetStatus.Disposed && a.WarrantyExpiry >= today && a.WarrantyExpiry <= until)
            .OrderBy(a => a.WarrantyExpiry)
            .Select(a => new
            {
                a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, a.WarrantyExpiry,
                Holder = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.DisplayName).FirstOrDefault(),
            })
            .Take(300).ToListAsync(cancellationToken);
        if (assets.Count == 0) return $"no warranties ending in the next {O.WarrantyDays} days";

        var lines = assets.Select(a => $"{a.AssetTag ?? a.SerialNumber} {a.Manufacturer} {a.Model}: ends {ReminderEmails.Date(a.WarrantyExpiry)}"
            + (a.Holder is null ? "" : $", with {a.Holder}")).ToList();
        foreach (var address in O.ItTeam)
            db.Notifications.Add(ReminderEmails.Compose("WarrantyList", address, null,
                $"GIIM: {assets.Count} warrant{(assets.Count == 1 ? "y ends" : "ies end")} in the next {O.WarrantyDays} days",
                $"These devices' warranties end in the next {O.WarrantyDays} days. The full list is in GIIM under Reports → Warranty.",
                [new EmailSection("Warranties ending", lines)], "Open GIIM", giim.Value.BaseUrl, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return $"{assets.Count} warranties, sent to {O.ItTeam.Count} address{(O.ItTeam.Count == 1 ? "" : "es")}";
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    /// <summary>A leaver's checklist assets that are still with them (not ones already reported lost or stolen: there's nothing to return).</summary>
    private async Task<List<string>> OutstandingAssetsAsync(Guid caseId, Guid personId, CancellationToken cancellationToken)
    {
        var assetIds = await db.ChecklistTasks.AsNoTracking()
            .Where(t => t.CaseId == caseId && t.Source == TaskSource.Asset && t.SourceId != null && t.Status != TaskState.Done && t.Status != TaskState.Skipped)
            .Select(t => t.SourceId!.Value).ToListAsync(cancellationToken);
        return await db.Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id) && a.AssignedToPersonId == personId
                && a.Status != AssetStatus.Lost && a.Status != AssetStatus.Stolen)
            .OrderBy(a => a.Model)
            .Select(a => (a.AssetTag ?? a.SerialNumber) + " " + a.Manufacturer + " " + a.Model)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Someone changed one of these records at the same moment: send nothing now, the next round catches up.</summary>
    private async Task<int> SaveAsync(int sent, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return sent;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return 0;
        }
    }
}
