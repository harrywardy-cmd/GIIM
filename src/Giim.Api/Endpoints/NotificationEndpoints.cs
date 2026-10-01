using Giim.Api.Security;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Api.Endpoints;

/// <summary>Emails and reminders: settings, scheduled runs, recent emails and "send now". Administrators only.</summary>
internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization(Policies.Administer);

        group.MapGet("", async (IOptions<ReminderOptions> options, IConfiguration config, GiimDbContext db, CancellationToken ct) =>
        {
            var o = options.Value;
            var runs = await db.ScheduledJobRuns.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
            var emails = await db.Notifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).Take(100)
                .Select(n => new { n.Id, n.Kind, n.ToName, n.ToAddress, n.Subject, n.Status, n.Attempts, n.CreatedAt, n.SentAt, n.LastError, n.RequestId })
                .ToListAsync(ct);
            return Results.Ok(new
            {
                EmailMode = config["Email:Mode"] ?? "None",
                o.Enabled,
                ItTeam = o.ItTeam,
                o.ManagerEmails,
                DigestTime = o.DigestTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                WarrantyDay = o.WarrantyDay.ToString(),
                o.DueSoonDays,
                o.WarrantyDays,
                ApprovalReminderAfterDays = o.ApprovalReminderAfter.TotalDays,
                o.MaxApprovalReminders,
                ReturnReminderEveryDays = o.ReturnReminderEvery.TotalDays,
                o.MaxReturnReminders,
                o.TimeZone,
                Runs = runs.Select(r => new { r.Name, r.LastRunOn, r.LastRunAt, r.LastResult }),
                Emails = emails,
            });
        });

        // Queues a digest now, e.g. to check it looks right; the workers send it within a minute.
        group.MapPost("/jobs/{job}/run", async (string job, ReminderService reminders, CancellationToken ct) =>
        {
            if (job is not (ReminderService.ItDigestJob or ReminderService.WarrantyJob)) return Results.NotFound();
            return Results.Ok(new { result = await reminders.RunJobAsync(job, ct) });
        });
    }
}
