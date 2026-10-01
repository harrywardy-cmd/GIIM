using Giim.Domain.Cases;
using Giim.Domain.Devices;
using Giim.Domain.Integration;
using Giim.Domain.Notifications;
using Giim.Domain.Requests;
using Giim.Infrastructure.Devices;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.People;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Attention;

public enum AttentionTarget { Request, Case, Page }

/// <summary>One thing to look at; opens a request, a checklist, or a page.</summary>
public sealed record AttentionItem(string Label, string? Detail, AttentionTarget Target, string Id);

/// <summary>
/// A kind of thing that needs someone. <paramref name="Severity"/> is "action" (it's waiting for you) or "problem"
/// (something went wrong). <paramref name="Page"/> is where the full list is.
/// </summary>
public sealed record AttentionGroup(string Key, string Title, string Severity, int Count, string Page, IReadOnlyList<AttentionItem> Items);

public sealed record AttentionSummary(int Total, IReadOnlyList<AttentionGroup> Groups);

/// <summary>
/// What needs the signed-in person now: worked out from the current state each time, so an item disappears as soon
/// as it is dealt with and there is nothing to mark as read. Each person sees only what their role acts on.
/// </summary>
public sealed class AttentionService(GiimDbContext db, IOptions<ReminderOptions> reminders, TimeProvider clock)
{
    private const int ItemsPerGroup = 5;
    private const string Action = "action";
    private const string Problem = "problem";

    public async Task<AttentionSummary> GetAsync(RequestActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var now = clock.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(reminders.Value.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var groups = new List<AttentionGroup?>();
        var isIt = actor.IsTechnician || actor.IsAdministrator;

        if (actor.IsManager || actor.IsAdministrator)
            groups.Add(await ApprovalsAsync(actor, now, cancellationToken));
        if (actor.IsManager || isIt)
            groups.Add(await QuestionsAsync(actor, cancellationToken));
        if (isIt)
        {
            groups.Add(await HandoversAsync(now, cancellationToken));
            groups.Add(await ChecklistsAsync(CaseType.Onboarding, today, cancellationToken));
            groups.Add(await ChecklistsAsync(CaseType.Offboarding, today, cancellationToken));
        }
        if (actor.IsAdministrator)
        {
            groups.Add(await TaskApprovalsAsync(cancellationToken));
            groups.Add(await TicketsAsync(now, cancellationToken));
            groups.Add(await SyncsAsync(now, cancellationToken));
            groups.Add(await EmailsAsync(now, cancellationToken));
        }

        var shown = groups.OfType<AttentionGroup>().ToList();
        return new AttentionSummary(shown.Sum(g => g.Count), shown);
    }

    // ---- Requests ----------------------------------------------------------------------------------------------------

    private async Task<AttentionGroup?> ApprovalsAsync(RequestActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waiting = RequestService.AwaitingApproval(db.DeviceRequests.AsNoTracking(), actor);
        var count = await waiting.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await waiting.OrderBy(r => r.SubmittedAt).Take(ItemsPerGroup)
            .Select(r => new { r.Id, r.Number, r.DeviceDescription, r.SubmittedAt, For = db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First() })
            .ToListAsync(cancellationToken);
        return new("approvals", "Waiting for your approval", Action, count, "approvals",
            [.. items.Select(r => Request(r.Id, $"{DeviceRequest.FormatReference(r.Number)} {r.DeviceDescription} for {r.For}", $"waiting {Age(now, r.SubmittedAt)}"))]);
    }

    private async Task<AttentionGroup?> QuestionsAsync(RequestActor actor, CancellationToken cancellationToken)
    {
        var asked = db.DeviceRequests.AsNoTracking().Where(r => r.Status == RequestStatus.InfoRequested && r.RequestedBy == actor.Login);
        var count = await asked.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await asked.OrderBy(r => r.UpdatedAt).Take(ItemsPerGroup)
            .Select(r => new { r.Id, r.Number, r.DeviceDescription, For = db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First() })
            .ToListAsync(cancellationToken);
        return new("questions", "The approver asked you a question", Action, count, "requests",
            [.. items.Select(r => Request(r.Id, $"{DeviceRequest.FormatReference(r.Number)} {r.DeviceDescription} for {r.For}", "answer it to continue"))]);
    }

    private async Task<AttentionGroup?> HandoversAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ready = db.DeviceRequests.AsNoTracking().Where(r => r.Status == RequestStatus.Approved || r.Status == RequestStatus.Received);
        var count = await ready.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await ready
            .OrderBy(r => r.NeededBy == null).ThenBy(r => r.NeededBy).ThenBy(r => r.DecidedAt)
            .Take(ItemsPerGroup)
            .Select(r => new { r.Id, r.Number, r.DeviceDescription, r.Status, r.DecidedAt, r.ReceivedAt, r.NeededBy, For = db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First() })
            .ToListAsync(cancellationToken);
        return new("handovers", "Devices to order or hand over", Action, count, "requests",
            [.. items.Select(r => Request(r.Id, $"{DeviceRequest.FormatReference(r.Number)} {r.DeviceDescription} for {r.For}",
                (r.Status == RequestStatus.Received ? $"received {Age(now, r.ReceivedAt)} ago, ready to hand over" : $"approved {Age(now, r.DecidedAt)} ago")
                + (r.NeededBy is { } needed ? $", needed {needed:d MMM}" : "")))]);
    }

    // ---- Starters and leavers -----------------------------------------------------------------------------------------

    private async Task<AttentionGroup?> ChecklistsAsync(CaseType type, DateOnly today, CancellationToken cancellationToken)
    {
        var soon = today.AddDays(reminders.Value.DueSoonDays);
        var due = db.Cases.AsNoTracking().Where(c => c.Type == type && c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled
            && c.DueDate != null && c.DueDate <= soon);
        var count = await due.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await due.OrderBy(c => c.DueDate).Take(ItemsPerGroup)
            .Select(c => new
            {
                c.Id, c.DueDate, Who = db.People.Where(p => p.Id == c.PersonId).Select(p => p.DisplayName).First(),
                Total = c.Tasks.Count,
                Done = c.Tasks.Count(t => t.Status == TaskState.Done || t.Status == TaskState.Skipped),
            })
            .ToListAsync(cancellationToken);
        var starters = type == CaseType.Onboarding;
        var overdue = items.Any(c => c.DueDate < today);
        return new(starters ? "starters" : "leavers",
            starters ? $"Starters in the next {reminders.Value.DueSoonDays} days" : $"Leavers in the next {reminders.Value.DueSoonDays} days",
            overdue ? Problem : Action, count, "cases",
            [.. items.Select(c => new AttentionItem(c.Who,
                $"{(starters ? "starts" : "last day")} {When(c.DueDate!.Value, today)}: {c.Done} of {c.Total} tasks done",
                AttentionTarget.Case, c.Id.ToString()))]);
    }

    private async Task<AttentionGroup?> TaskApprovalsAsync(CancellationToken cancellationToken)
    {
        var waiting = db.ChecklistTasks.AsNoTracking().Where(t => t.RequiresApproval && t.ApprovedAt == null
            && t.Status != TaskState.Done && t.Status != TaskState.Skipped
            && db.Cases.Any(c => c.Id == t.CaseId && c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled));
        var count = await waiting.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await waiting.OrderBy(t => t.CreatedAt).Take(ItemsPerGroup)
            .Select(t => new { t.CaseId, t.Title, Who = db.Cases.Where(c => c.Id == t.CaseId).Select(c => db.People.Where(p => p.Id == c.PersonId).Select(p => p.DisplayName).First()).First() })
            .ToListAsync(cancellationToken);
        return new("task-approvals", "Checklist steps for you to approve", Action, count, "cases",
            [.. items.Select(t => new AttentionItem(t.Title, t.Who, AttentionTarget.Case, t.CaseId.ToString()))]);
    }

    // ---- Things that went wrong (administrators) ----------------------------------------------------------------------

    private async Task<AttentionGroup?> TicketsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-14);
        // A ticket that later went through (e.g. once its department was added) no longer counts.
        var stuck = db.ServiceDeskInbound.AsNoTracking().Where(e => (e.Status == InboundStatus.NeedsAttention || e.Status == InboundStatus.Failed)
            && e.CreatedAt >= since
            && !db.ServiceDeskInbound.Any(later => later.RequestKey == e.RequestKey && later.Status == InboundStatus.Processed && later.CreatedAt > e.CreatedAt));
        var count = await stuck.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await stuck.OrderByDescending(e => e.CreatedAt).Take(ItemsPerGroup)
            .Select(e => new { e.DisplayId, e.RequestKey, e.Message }).ToListAsync(cancellationToken);
        return new("tickets", "ServiceDesk tickets GIIM couldn't use", Problem, count, "servicedesk",
            [.. items.Select(e => Page($"Ticket {e.DisplayId ?? e.RequestKey}", e.Message, "servicedesk"))]);
    }

    private async Task<AttentionGroup?> SyncsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = new List<AttentionItem>();
        foreach (var (source, name, page) in new[] { (IntuneSyncService.Source, "Intune sync", "reconciliation"), (PeopleSyncService.Source, "Staff directory sync", "people") })
        {
            var last = await db.SyncRuns.AsNoTracking().Where(r => r.Source == source && r.Status != SyncRunStatus.Running)
                .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);
            if (last?.Status == SyncRunStatus.Failed)
                failed.Add(Page($"{name} failed {Age(now, last.StartedAt)} ago", last.Error, page));
        }
        return failed.Count == 0 ? null : new("syncs", "Syncs that failed last time", Problem, failed.Count, failed[0].Id, failed);
    }

    private async Task<AttentionGroup?> EmailsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-7);
        var failed = db.Notifications.AsNoTracking().Where(n => n.Status == NotificationStatus.Failed && n.CreatedAt >= since);
        var count = await failed.CountAsync(cancellationToken);
        if (count == 0) return null;
        var items = await failed.OrderByDescending(n => n.CreatedAt).Take(ItemsPerGroup)
            .Select(n => new { n.Subject, n.ToAddress, n.LastError }).ToListAsync(cancellationToken);
        return new("emails", "Emails that couldn't be sent (last 7 days)", Problem, count, "notifications",
            [.. items.Select(n => Page(n.Subject, $"to {n.ToAddress}{(n.LastError is null ? "" : $": {n.LastError}")}", "notifications"))]);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private static AttentionItem Request(Guid id, string label, string detail) => new(label, detail, AttentionTarget.Request, id.ToString());
    private static AttentionItem Page(string label, string? detail, string page) =>
        new(label, detail is { Length: > 200 } text ? text[..200] + "…" : detail, AttentionTarget.Page, page);

    /// <summary>How long ago, e.g. "3 days", "5 h", "under an hour".</summary>
    private static string Age(DateTimeOffset now, DateTimeOffset? then)
    {
        var age = now - (then ?? now);
        return age.TotalHours < 1 ? "under an hour"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h"
            : (int)age.TotalDays == 1 ? "1 day" : $"{(int)age.TotalDays} days";
    }

    private static string When(DateOnly date, DateOnly today) =>
        date == today ? "today"
        : date == today.AddDays(1) ? "tomorrow"
        : date < today ? $"{date:ddd d MMM} (overdue)"
        : $"{date:ddd d MMM}";
}
