using System.Net;
using System.Text;
using Azure.Core;
using Giim.Connectors.People;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

/// <summary>How Entra ID accounts become staff records, against simulated Graph responses (no real tenant needed).</summary>
public class EntraPeopleSourceTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class FakeGraph(string json) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        // 10am in Sydney on Today.
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    }

    private const string Users = """
        {"value":[
          {"id":"6f1c3a52-8f0e-4c7e-9a43-111111111111","displayName":"Sam Lee","userPrincipalName":"Sam.Lee@Contoso.com",
           "mail":"Sam.Lee@contoso.com","employeeId":" E100 ","department":"Finance","jobTitle":"Accountant","officeLocation":"Sydney",
           "accountEnabled":true,"employeeHireDate":"2020-02-03T00:00:00Z",
           "onPremisesExtensionAttributes":{"extensionAttribute5":"Light"},
           "manager":{"@odata.type":"#microsoft.graph.user","id":"x","userPrincipalName":"Boss@Contoso.com"}},
          {"id":"6f1c3a52-8f0e-4c7e-9a43-222222222222","displayName":"Boardroom Level 3","userPrincipalName":"room.l3@contoso.com",
           "accountEnabled":true},
          {"id":"6f1c3a52-8f0e-4c7e-9a43-333333333333","displayName":"Old Leaver","userPrincipalName":"old.leaver@contoso.com",
           "employeeId":"E200","accountEnabled":false},
          {"id":"6f1c3a52-8f0e-4c7e-9a43-444444444444","displayName":"New Starter","userPrincipalName":"new.starter@contoso.com",
           "employeeId":"E300","department":"Finance","accountEnabled":false,"employeeHireDate":"2026-10-12T13:00:00Z",
           "employeeLeaveDateTime":"2027-01-01T00:00:00Z"}
        ]}
        """;

    private static (EntraPeopleSource Source, FakeGraph Graph) Create(Action<EntraDirectoryOptions>? configure = null, string json = Users)
    {
        var options = new PeopleOptions { Source = PeopleSource.Entra };
        options.Entra.TrackAttribute = "extensionAttribute5";
        configure?.Invoke(options.Entra);
        var graph = new FakeGraph(json);
        return (new EntraPeopleSource(new HttpClient(graph), new FakeCredential(), Options.Create(options), new FixedTime(),
            NullLogger<EntraPeopleSource>.Instance), graph);
    }

    private static async Task<List<DirectoryPerson>> ReadAll(EntraPeopleSource source)
    {
        var people = new List<DirectoryPerson>();
        await foreach (var p in source.GetPeopleAsync()) people.Add(p);
        return people;
    }

    [Fact]
    public async Task Maps_accounts_to_staff_and_skips_accounts_without_an_employee_id()
    {
        var people = await ReadAll(Create().Source);

        Assert.Equal("E100,E200,E300", string.Join(',', people.Select(p => p.EmployeeId)));
        var sam = people[0];
        Assert.Equal("Sam Lee", sam.DisplayName);
        Assert.Equal("sam.lee@contoso.com", sam.UserPrincipalName);
        Assert.Equal("sam.lee@contoso.com", sam.Email);
        Assert.Equal(Guid.Parse("6f1c3a52-8f0e-4c7e-9a43-111111111111"), sam.EntraObjectId);
        Assert.Equal(("FINANCE", "Finance"), (sam.DepartmentCode, sam.DepartmentName));
        Assert.Equal(("Accountant", "Sydney"), (sam.JobTitle, sam.Location));
        Assert.Equal("Active", sam.Status);
        Assert.Equal(new DateOnly(2020, 2, 3), sam.StartDate);
        Assert.Equal("Light", sam.Track);
        Assert.Equal("boss@contoso.com", sam.ManagerUserPrincipalName);
    }

    [Fact]
    public async Task Disabled_accounts_have_left_unless_they_have_not_started_yet()
    {
        var people = await ReadAll(Create().Source);

        var leaver = people.Single(p => p.EmployeeId == "E200");
        Assert.Equal("Left", leaver.Status);
        Assert.Equal(EntraPeopleSource.NoDepartmentCode, leaver.DepartmentCode);

        var starter = people.Single(p => p.EmployeeId == "E300");
        Assert.Equal("Pending", starter.Status);
        // 13:00 UTC on the 12th is midnight on the 13th in Sydney (daylight saving).
        Assert.Equal(new DateOnly(2026, 10, 13), starter.StartDate);
        Assert.Null(starter.EndDate);   // leave dates are only read when switched on
    }

    [Fact]
    public async Task Leave_dates_are_read_only_when_switched_on()
    {
        var (source, graph) = Create(o => o.ReadLeaveDates = true);

        var starter = (await ReadAll(source)).Single(p => p.EmployeeId == "E300");

        Assert.Equal(new DateOnly(2027, 1, 1), starter.EndDate);
        Assert.Contains("employeeLeaveDateTime", Query(graph), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asks_for_members_only_with_their_manager_and_a_bearer_token()
    {
        var (source, graph) = Create(o => o.Filter = "companyName eq 'Contoso'");

        await ReadAll(source);

        var request = Assert.Single(graph.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.StartsWith("https://graph.microsoft.com/v1.0/users?", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        var query = Query(graph);
        Assert.Contains("$expand=manager($select=id,userPrincipalName)", query, StringComparison.Ordinal);
        Assert.Contains("$filter=userType eq 'Member' and (companyName eq 'Contoso')", query, StringComparison.Ordinal);
        Assert.DoesNotContain("employeeLeaveDateTime", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, null, null, "Active")]
    [InlineData(null, null, null, "Active")]
    [InlineData(false, null, null, "Left")]
    [InlineData(false, "2026-10-12", null, "Pending")]       // account created ahead of the start date
    [InlineData(true, "2026-10-12", null, "Pending")]
    [InlineData(true, "2026-10-02", null, "Active")]         // starts today
    [InlineData(true, null, "2026-10-30", "Leaving")]
    [InlineData(true, null, "2026-10-02", "Leaving")]        // last day today
    [InlineData(true, null, "2026-10-01", "Left")]           // leave date passed, account not yet disabled
    [InlineData(false, null, "2026-10-30", "Left")]
    public void Status_comes_from_the_account_and_its_dates(bool? enabled, string? start, string? end, string expected)
    {
        DateOnly? Parse(string? d) => d is null ? null : DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, EntraPeopleSource.Status(enabled, Parse(start), Parse(end), Today));
    }

    [Fact]
    public void Department_codes_are_made_from_names_and_fit_the_column()
    {
        var none = new Dictionary<string, string>();
        Assert.Equal("FINANCE", EntraPeopleSource.DepartmentCode("Finance", none));
        Assert.Equal(EntraPeopleSource.NoDepartmentCode, EntraPeopleSource.DepartmentCode(null, none));

        var it = EntraPeopleSource.DepartmentCode("Information Technology", none);
        var itServices = EntraPeopleSource.DepartmentCode("Information Technology Services", none);
        Assert.True(it.Length <= 20 && itServices.Length <= 20);
        Assert.StartsWith("INFORMATION-TEC", it, StringComparison.Ordinal);
        Assert.NotEqual(it, itServices);
        Assert.Equal(it, EntraPeopleSource.DepartmentCode("Information Technology", none));   // the same every sync
        Assert.NotEqual(EntraPeopleSource.DepartmentCode("R&D", none), EntraPeopleSource.DepartmentCode("R D", none));
    }

    [Fact]
    public void Configured_department_codes_win_and_are_checked()
    {
        var map = EntraPeopleSource.DepartmentCodeMap(new Dictionary<string, string> { ["Human Resources"] = " hr " });
        Assert.Equal("HR", EntraPeopleSource.DepartmentCode("human resources", map));

        Assert.Throws<InvalidOperationException>(() =>
            EntraPeopleSource.DepartmentCodeMap(new Dictionary<string, string> { ["Finance"] = "FINANCE-AND-ACCOUNTING" }));
    }

    private static string Query(FakeGraph graph) => Uri.UnescapeDataString(graph.Requests[0].RequestUri!.Query);
}
