using System.Net;
using System.Text;
using Azure.Core;
using Giim.Connectors.CloudAccounts;
using Giim.Domain.Assets;
using Giim.Infrastructure.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

/// <summary>Adding a synced account to a cloud-only Entra group, against simulated Graph responses.</summary>
public class GraphCloudDirectoryTests
{
    private static readonly Guid Account = Guid.Parse("8ff3b88a-3937-4e86-ae52-700fdfc88cb3");

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("t", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class FakeGraph(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode, string)> _answers = new(answers);
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, Uri.UnescapeDataString(request.RequestUri!.ToString()),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            var (status, body) = _answers.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (GraphCloudDirectory Directory, FakeGraph Graph) Create(params (HttpStatusCode, string)[] answers)
    {
        var graph = new FakeGraph(answers);
        return (new GraphCloudDirectory(new HttpClient(graph), new FakeCredential(), Options.Create(new CloudDirectoryOptions { MaxRetries = 0 }),
            NullLogger<GraphCloudDirectory>.Instance), graph);
    }

    private static (HttpStatusCode, string) Group(string json) => (HttpStatusCode.OK, $$"""{"value":[{{json}}]}""");
    private const string CloudGroup = """{"id":"g1","displayName":"Finance Team","onPremisesSyncEnabled":null,"groupTypes":["Unified"],"isAssignableToRole":false}""";

    [Fact]
    public async Task Adds_the_account_to_the_group_found_by_name()
    {
        var (directory, graph) = Create(Group(CloudGroup), (HttpStatusCode.NoContent, ""));

        Assert.True(await directory.AddToGroupAsync(Account, "Finance Team", CancellationToken.None));

        Assert.Contains("$filter=displayName eq 'Finance Team'", graph.Requests[0].Url, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, graph.Requests[1].Method);
        Assert.EndsWith("groups/g1/members/$ref", graph.Requests[1].Url, StringComparison.Ordinal);
        Assert.Contains($"directoryObjects/{Account}", graph.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Already_a_member_is_not_an_error()
    {
        var (directory, _) = Create(Group(CloudGroup),
            (HttpStatusCode.BadRequest, """{"error":{"message":"One or more added object references already exist for the following modified properties: 'members'."}}"""));

        Assert.False(await directory.AddToGroupAsync(Account, "Finance Team", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"id":"g1","displayName":"SG-Finance","onPremisesSyncEnabled":true}""", "synced from AD")]
    [InlineData("""{"id":"g1","displayName":"Helpdesk Admins","isAssignableToRole":true}""", "grants admin roles")]
    [InlineData("""{"id":"g1","displayName":"All Sydney","groupTypes":["DynamicMembership"]}""", "dynamic membership")]
    public async Task Groups_giim_must_not_change_are_refused_before_any_change(string group, string reason)
    {
        var (directory, graph) = Create(Group(group));

        var e = await Assert.ThrowsAsync<CloudDirectoryException>(() => directory.AddToGroupAsync(Account, "x", CancellationToken.None));

        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.False(e.Retryable);
        Assert.Single(graph.Requests);   // looked it up, changed nothing
    }

    [Fact]
    public async Task A_missing_group_or_one_outside_giims_reach_explains_itself()
    {
        var (missing, _) = Create((HttpStatusCode.OK, """{"value":[]}"""));
        Assert.Contains("no Entra group", (await Assert.ThrowsAsync<CloudDirectoryException>(
            () => missing.AddToGroupAsync(Account, "Nope", CancellationToken.None))).Message, StringComparison.Ordinal);

        var (forbidden, _) = Create(Group(CloudGroup), (HttpStatusCode.Forbidden, """{"error":{"code":"Authorization_RequestDenied"}}"""));
        Assert.Contains("administrative unit", (await Assert.ThrowsAsync<CloudDirectoryException>(
            () => forbidden.AddToGroupAsync(Account, "Finance Team", CancellationToken.None))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quotes_in_a_group_name_cannot_change_the_search()
    {
        var (directory, graph) = Create((HttpStatusCode.OK, """{"value":[]}"""));

        await Assert.ThrowsAsync<CloudDirectoryException>(() => directory.AddToGroupAsync(Account, "O'Brien' or 1 eq 1", CancellationToken.None));

        Assert.Contains("displayName eq 'O''Brien'' or 1 eq 1'", graph.Requests[0].Url, StringComparison.Ordinal);
    }
}

public class DashboardTotalsTests
{
    [Fact]
    public void Totals_group_the_statuses_the_dashboard_shows()
    {
        var totals = DashboardTotals.From(new Dictionary<AssetStatus, int>
        {
            [AssetStatus.Assigned] = 10, [AssetStatus.ReturnRequested] = 2, [AssetStatus.ReadyToDeploy] = 5, [AssetStatus.InRepair] = 1,
            [AssetStatus.Lost] = 1, [AssetStatus.Retired] = 3, [AssetStatus.Disposed] = 4,
        }, pendingApproval: 6);

        Assert.Equal(new DashboardTotals(InService: 19, Assigned: 12, Available: 5, InRepair: 1, Retired: 3, Disposed: 4, PendingApproval: 6), totals);
    }
}
