using System.Net;
using System.Text;
using Azure.Core;
using Giim.Connectors.Intune;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Tests;

/// <summary>Checks paging, auth and throttling against simulated Graph responses (no real tenant needed).</summary>
public class GraphIntuneClientTests
{
    private sealed class FakeCredential : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https://graph.microsoft.com/.default", Assert.Single(requestContext.Scopes));
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }

    /// <summary>Returns queued responses in order and records every request.</summary>
    private sealed class FakeGraph(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private static Func<HttpResponseMessage> Page(string serials, string? nextLink = null) => () =>
    {
        var devices = string.Join(',', serials.Split(',').Select(s =>
            $$"""{"id":"id-{{s}}","deviceName":"PC-{{s}}","serialNumber":"{{s}}","lastSyncDateTime":"2026-09-28T00:00:00Z"}"""));
        var next = nextLink is null ? "" : $$""","@odata.nextLink":"{{nextLink}}" """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"value":[{{devices}}]{{next}}}""", Encoding.UTF8, "application/json"),
        };
    };

    private static Func<HttpResponseMessage> Status(HttpStatusCode code, int? retryAfterSeconds = 0) => () =>
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent("""{"error":{"code":"x"}}""") };
        if (retryAfterSeconds is { } s) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
        return response;
    };

    private static (GraphIntuneClient Client, FakeGraph Graph, FakeCredential Credential) Create(int maxRetries, params Func<HttpResponseMessage>[] responses)
    {
        var graph = new FakeGraph(responses);
        var credential = new FakeCredential();
        var options = Options.Create(new IntuneOptions { Source = IntuneSource.Graph, MaxRetries = maxRetries });
        return (new GraphIntuneClient(new HttpClient(graph), credential, options, NullLogger<GraphIntuneClient>.Instance), graph, credential);
    }

    private static async Task<List<IntuneDevice>> ReadAll(GraphIntuneClient client)
    {
        var devices = new List<IntuneDevice>();
        await foreach (var d in client.GetManagedDevicesAsync()) devices.Add(d);
        return devices;
    }

    [Fact]
    public async Task Follows_next_links_until_the_last_page()
    {
        var (client, graph, _) = Create(3,
            Page("A,B", nextLink: "https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=p2"),
            Page("C"));

        var devices = await ReadAll(client);

        Assert.Equal(["A", "B", "C"], devices.Select(d => d.SerialNumber));
        Assert.Equal(2, graph.Requests.Count);
        Assert.Contains("$skiptoken=p2", graph.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_only_the_needed_fields_with_a_bearer_token()
    {
        var (client, graph, credential) = Create(3, Page("A"));

        await ReadAll(client);

        var request = Assert.Single(graph.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("fake-token", request.Headers.Authorization?.Parameter);
        Assert.Contains("$select=id,deviceName,serialNumber", Uri.UnescapeDataString(request.RequestUri!.Query), StringComparison.Ordinal);
        Assert.Equal(1, credential.Calls);
    }

    [Fact]
    public async Task Retries_when_throttled_then_succeeds()
    {
        var (client, graph, _) = Create(3, Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.ServiceUnavailable), Page("A"));

        var devices = await ReadAll(client);

        Assert.Single(devices);
        Assert.Equal(3, graph.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var (client, graph, _) = Create(2,
            Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.TooManyRequests));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ReadAll(client));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(3, graph.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_permission_errors()
    {
        var (client, graph, _) = Create(5, Status(HttpStatusCode.Forbidden, retryAfterSeconds: null));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ReadAll(client));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Single(graph.Requests);
    }
}
