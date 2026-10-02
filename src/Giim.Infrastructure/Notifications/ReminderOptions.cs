namespace Giim.Infrastructure.Notifications;

/// <summary>Reminders and digests (section "Reminders"). All times are local to <see cref="TimeZone"/>.</summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    public bool Enabled { get; set; } = true;

    /// <summary>Who gets the daily IT digest and weekly warranty list, separated by ; or , (e.g. a team mailbox).</summary>
    public string? ItTeamAddresses { get; set; }

    /// <summary>Email a starter's or leaver's manager when their checklist is created.</summary>
    public bool ManagerEmails { get; set; } = true;

    /// <summary>Email the person whose device goes for repair, and again when it's done.</summary>
    public bool RepairEmails { get; set; } = true;

    public TimeOnly DigestTime { get; set; } = new(7, 30);
    public DayOfWeek WarrantyDay { get; set; } = DayOfWeek.Monday;

    /// <summary>Starters and leavers this many days ahead count as "due soon" in the digest.</summary>
    public int DueSoonDays { get; set; } = 3;
    public int WarrantyDays { get; set; } = 30;

    public TimeSpan ApprovalReminderAfter { get; set; } = TimeSpan.FromDays(2);
    public int MaxApprovalReminders { get; set; } = 3;

    public TimeSpan ReturnReminderEvery { get; set; } = TimeSpan.FromDays(7);
    public int MaxReturnReminders { get; set; } = 3;

    public string TimeZone { get; set; } = "Australia/Sydney";

    public IReadOnlyList<string> ItTeam =>
        (ItTeamAddresses ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Contains('@', StringComparison.Ordinal)).ToList();
}

/// <summary>When scheduled jobs are due: pure rules, so they can be tested without a clock or database.</summary>
public static class ReminderSchedule
{
    /// <summary>Due once the local time reaches <paramref name="at"/>, if it hasn't already run today.</summary>
    public static bool DailyDue(DateTime localNow, TimeOnly at, DateOnly? lastRunOn) =>
        TimeOnly.FromDateTime(localNow) >= at && lastRunOn != DateOnly.FromDateTime(localNow);

    /// <summary>Due on <paramref name="day"/> once the local time reaches <paramref name="at"/>, if it hasn't already run today.</summary>
    public static bool WeeklyDue(DateTime localNow, DayOfWeek day, TimeOnly at, DateOnly? lastRunOn) =>
        localNow.DayOfWeek == day && DailyDue(localNow, at, lastRunOn);

    /// <summary>
    /// A repeating reminder: the first is due at <paramref name="firstAt"/>, then one every <paramref name="every"/>, up to
    /// <paramref name="max"/> in all.
    /// </summary>
    public static bool ReminderDue(DateTimeOffset now, DateTimeOffset firstAt, DateTimeOffset? lastSent, int sent, TimeSpan every, int max) =>
        sent < max && now >= firstAt && (lastSent is null || now - lastSent.Value >= every);
}
