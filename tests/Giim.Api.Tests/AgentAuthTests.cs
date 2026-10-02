using System.Net;
using System.Net.Http.Json;
using System.Text;
using Giim.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Giim.Api.Tests;

/// <summary>
/// The on-prem agent's endpoints accept only the agent's own credential (the development key here), never a staff
/// session; and only IT can start checklist automation. Every request is refused or rejected before the database.
/// </summary>
public class AgentAuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevKey = "dev-agent-key-only-works-on-a-developer-pc";

    /// <summary>Not a JSON object: once signed in, binding refuses it with 400 before any database work.</summary>
    private static StringContent Unreadable() => new("\"not a request\"", Encoding.UTF8, "application/json");

    private HttpClient Agent(string? key)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (key is not null) client.DefaultRequestHeaders.Add(AgentAuth.KeyHeader, key);
        return client;
    }

    private async Task<HttpClient> Staff(string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/auth/dev-login", new { name = $"test.{role.ToLowerInvariant()}", role });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "1");
        return client;
    }

    [Theory]
    [InlineData("/agent/check-in")]
    [InlineData("/agent/jobs/claim")]
    public async Task Agent_endpoints_need_the_agent_key(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Agent(null).PostAsync(path, Unreadable())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Agent("wrong-key-wrong-key-wrong-key-wrong-key").PostAsync(path, Unreadable())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Agent(DevKey).PostAsync(path, Unreadable())).StatusCode);
    }

    [Fact]
    public async Task A_staff_session_cannot_act_as_the_agent()
    {
        var admin = await Staff("Administrator");

        var response = await admin.PostAsync($"/agent/jobs/{Guid.NewGuid()}/result", Unreadable());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_agent_key_does_not_open_the_staff_api()
    {
        var response = await Agent(DevKey).GetAsync("/api/automation");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Manager")]
    public async Task Only_it_starts_checklist_automation(string role)
    {
        var client = await Staff(role);

        var response = await client.PostAsync($"/api/cases/{Guid.NewGuid()}/automation/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
