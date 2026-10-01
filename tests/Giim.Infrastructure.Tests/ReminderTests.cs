using Giim.Infrastructure.Notifications;

namespace Giim.Infrastructure.Tests;

public class ReminderTests
{
    private static readonly TimeOnly HalfSevenAm = new(7, 30);
    private static readonly DateTime Thursday0729 = new(2026, 10, 1, 7, 29, 0);
    private static readonly DateTime Thursday0730 = new(2026, 10, 1, 7, 30, 0);
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Daily_digest_waits_for_its_time_then_runs_once_that_day()
    {
        Assert.False(ReminderSchedule.DailyDue(Thursday0729, HalfSevenAm, lastRunOn: null));
        Assert.True(ReminderSchedule.DailyDue(Thursday0730, HalfSevenAm, lastRunOn: Today.AddDays(-1)));
        Assert.False(ReminderSchedule.DailyDue(Thursday0730.AddHours(5), HalfSevenAm, lastRunOn: Today));   // already ran today
        Assert.True(ReminderSchedule.DailyDue(Thursday0730.AddDays(1), HalfSevenAm, lastRunOn: Today));      // tomorrow again
    }

    [Fact]
    public void Weekly_list_runs_only_on_its_day()
    {
        Assert.True(ReminderSchedule.WeeklyDue(Thursday0730, DayOfWeek.Thursday, HalfSevenAm, null));
        Assert.False(ReminderSchedule.WeeklyDue(Thursday0730, DayOfWeek.Monday, HalfSevenAm, null));
        Assert.False(ReminderSchedule.WeeklyDue(Thursday0730, DayOfWeek.Thursday, HalfSevenAm, Today));
    }

    [Fact]
    public void Repeating_reminders_start_on_time_space_out_and_stop_at_the_limit()
    {
        var first = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(10));
        var every = TimeSpan.FromDays(2);

        Assert.False(ReminderSchedule.ReminderDue(first.AddMinutes(-1), first, null, 0, every, 3));    // not yet
        Assert.True(ReminderSchedule.ReminderDue(first, first, null, 0, every, 3));                    // first one
        Assert.False(ReminderSchedule.ReminderDue(first.AddDays(1), first, first, 1, every, 3));       // too soon after
        Assert.True(ReminderSchedule.ReminderDue(first.AddDays(2), first, first, 1, every, 3));        // next one
        Assert.False(ReminderSchedule.ReminderDue(first.AddDays(30), first, first.AddDays(4), 3, every, 3));   // limit reached
    }

    [Theory]
    [InlineData("it@co.au; desk@co.au", 2)]
    [InlineData("it@co.au,desk@co.au", 2)]
    [InlineData(" it@co.au ;; not-an-address ", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void It_team_addresses_are_read_from_one_setting(string? setting, int expected)
    {
        Assert.Equal(expected, new ReminderOptions { ItTeamAddresses = setting }.ItTeam.Count);
    }

    [Fact]
    public void Emails_list_their_sections_and_encode_what_people_typed()
    {
        var email = ReminderEmails.Compose("ItDigest", "it@co.au", null, "GIIM daily summary", "Here's what needs attention today.",
            [new EmailSection("Starting soon", ["<b>Sam</b> & co, Mon 5 Oct 2026: 2/10 tasks done"]), new EmailSection("Empty", [])],
            "Open GIIM", "https://giim.example", DateTimeOffset.UtcNow);

        Assert.Contains("- <b>Sam</b> & co, Mon 5 Oct 2026: 2/10 tasks done", email.BodyText, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Sam&lt;/b&gt; &amp; co", email.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Sam</b>", email.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Empty", email.BodyHtml, StringComparison.Ordinal);   // empty sections are left out
        Assert.Contains("href=\"https://giim.example\"", email.BodyHtml, StringComparison.Ordinal);
    }
}
