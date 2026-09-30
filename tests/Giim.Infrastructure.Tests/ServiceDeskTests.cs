using System.Text.Json;
using Giim.Connectors.ServiceDesk;
using Giim.Infrastructure.ServiceDesk;

namespace Giim.Infrastructure.Tests;

public class ServiceDeskTests
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    private static ServiceDeskRequest SampleTicket(int index)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "samples", "servicedesk-requests.json")));
        return ServiceDeskJson.ParseRequest(json.RootElement.GetProperty("requests")[index]);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Ticket_is_read_with_its_template_requester_and_custom_fields()
    {
        var ticket = SampleTicket(0);

        Assert.Equal("123456000000101", ticket.Key);
        Assert.Equal("5601", ticket.DisplayId);
        Assert.Equal("New Starter", ticket.Template);
        Assert.Equal("Open", ticket.Status);
        Assert.Equal("hr@giim-test.local", ticket.RequesterEmail);
        Assert.Equal("E109001", ticket.Fields["udf_sline_301"]);
        Assert.Equal("1792328400000", ticket.Fields["udf_date_306"]);   // SDP dates arrive as milliseconds
    }

    [Theory]
    [InlineData("1792328400000", 2026, 10, 19)]   // midnight 19 Oct in Sydney (daylight saving), 18 Oct in UTC
    [InlineData("2026-10-19", 2026, 10, 19)]
    [InlineData("19/10/2026", 2026, 10, 19)]
    [InlineData("19 Oct 2026", 2026, 10, 19)]
    public void Ticket_dates_are_read_in_sydney_time(string value, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), ServiceDeskJson.ParseDate(value, Sydney));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("next Monday")]
    public void Unreadable_dates_are_not_guessed(string? value)
    {
        Assert.Null(ServiceDeskJson.ParseDate(value, Sydney));
    }

    [Theory]
    [InlineData("5501", "5501")]
    [InlineData("REQ5501", "5501")]
    [InlineData("#5501", "5501")]
    [InlineData("SDP-5501", "5501")]
    public void Ticket_numbers_are_matched_on_their_digits(string typed, string expected)
    {
        Assert.Equal(expected, ServiceDeskSync.TicketDigits(typed));
    }

    [Fact]
    public void Lookup_fields_are_read_by_name()
    {
        using var json = JsonDocument.Parse("""{"id": 7, "display_id": "9", "subject": "x", "udf_fields": {"udf_pick_1": {"name": "Finance"}}}""");

        var ticket = ServiceDeskJson.ParseRequest(json.RootElement);

        Assert.Equal("7", ticket.Key);
        Assert.Equal("Finance", ticket.Fields["udf_pick_1"]);
    }
}
