using System.Net;
using System.Net.Http.Json;
using Giim.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Giim.Api.Tests;

/// <summary>
/// Hosts the real API in memory (Development sign-in) and checks each role gets exactly the access it should.
/// Every request here is refused or rejected before any database access, so no database is needed.
/// </summary>
public class AuthorisationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> SignedInAs(string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/auth/dev-login", new { name = $"test.{role.ToLowerInvariant()}", role });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "1");
        return client;
    }

    [Fact]
    public async Task Api_needs_sign_in()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/assets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);   // 401, not a redirect to a login page
    }

    [Fact]
    public async Task Sign_in_configuration_is_public()
    {
        var config = await factory.CreateClient().GetFromJsonAsync<Dictionary<string, object>>("/api/auth/config");

        Assert.Equal("Development", config!["mode"].ToString());
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Manager")]
    [InlineData("Technician")]
    [InlineData("Administrator")]
    public async Task Me_reports_the_signed_in_role(string role)
    {
        var client = await SignedInAs(role);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal($"test.{role.ToLowerInvariant()}", me!.Login);
        Assert.Equal([role], me.Roles);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Manager")]
    public async Task Read_only_roles_cannot_change_assets(string role)
    {
        var client = await SignedInAs(role);

        var response = await client.PostAsJsonAsync($"/api/assets/{Guid.NewGuid()}/actions", new { action = "Move" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Technician_can_change_assets()
    {
        var client = await SignedInAs("Technician");

        // Passes authorisation and reaches the endpoint, which rejects the incomplete request itself.
        var response = await client.PostAsJsonAsync($"/api/assets/{Guid.NewGuid()}/actions", new { action = "Move" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Technician_cannot_change_set_up_lists()
    {
        var client = await SignedInAs("Technician");

        var response = await client.PostAsJsonAsync("/api/locations", new { name = "", kind = "Office", holdsStock = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_can_change_set_up_lists()
    {
        var client = await SignedInAs("Administrator");

        var response = await client.PostAsJsonAsync("/api/locations", new { name = "", kind = "Office", holdsStock = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);   // reached the endpoint; blank name refused
    }

    [Fact]
    public async Task Changes_without_the_request_header_are_refused()
    {
        var client = await SignedInAs("Administrator");
        client.DefaultRequestHeaders.Remove(AuthSetup.CsrfHeader);

        var response = await client.PostAsJsonAsync("/api/locations", new { name = "X", kind = "Office", holdsStock = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(AuthSetup.CsrfHeader, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_sign_in_rejects_unknown_roles()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/dev-login", new { name = "x", role = "SuperUser" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Development_sign_in_cannot_run_outside_development()
    {
        using var production = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Giim", "Server=unused")   // Production doesn't load the dev settings file
            .UseSetting("Auth:Mode", "Development"));

        var error = Assert.ThrowsAny<Exception>(() => production.CreateClient());
        Assert.Contains("only allowed in the Development environment", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Secure by default: a new endpoint that changes data can never be left open by accident.</summary>
    [Fact]
    public void Every_api_endpoint_that_changes_data_requires_technician()
    {
        var endpoints = ApiEndpoints().ToList();
        Assert.NotEmpty(endpoints);

        foreach (var (route, methods, policies) in endpoints)
        {
            Assert.True(policies.Contains(Policies.Read) || route is "/api/me" or "/api/auth/config", $"{route} is not protected");
            if (methods.Any(m => m is not ("GET" or "HEAD")))
                Assert.True(policies.Contains(Policies.Change), $"{string.Join(",", methods)} {route} does not require Technician");
        }
    }

    [Fact]
    public void Set_up_changes_require_administrator()
    {
        var adminRoutes = ApiEndpoints()
            .Where(e => e.Methods.Any(m => m != "GET") && AuthSetup.AdministratorOnly.Any(p => e.Route.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(adminRoutes);
        Assert.All(adminRoutes, e => Assert.Contains(Policies.Administer, e.Policies));
    }

    private IEnumerable<(string Route, string[] Methods, string[] Policies)> ApiEndpoints()
    {
        _ = factory.Server; // make sure the host (and its endpoints) have been built
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Select(e => (
                e.RoutePattern.RawText!,
                e.Metadata.OfType<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods).ToArray(),
                e.Metadata.OfType<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToArray()));
    }

    private sealed record MeResponse(string? Name, string? Login, string? Email, string[] Roles);
}

public class AuthHelperTests
{
    private static readonly Dictionary<string, string> RoleGroups = new()
    {
        [Roles.Administrator] = "GIIM-Administrators",
        [Roles.Technician] = "GIIM-Technicians",
        [Roles.Manager] = "GIIM-Managers",
        [Roles.Viewer] = "GIIM-Viewers",
    };

    [Fact]
    public void Okta_groups_map_to_roles_ignoring_case_and_other_groups()
    {
        var roles = GroupRoleMapper.RolesFor(["Everyone", "giim-technicians", "Finance-Staff", "GIIM-Viewers"], RoleGroups);

        Assert.Equal([Roles.Technician, Roles.Viewer], roles);
    }

    [Fact]
    public void Someone_in_no_giim_group_gets_no_role()
    {
        Assert.Empty(GroupRoleMapper.RolesFor(["Everyone", "Finance-Staff"], RoleGroups));
    }

    [Theory]
    [InlineData("/?asset=123", "/?asset=123")]
    [InlineData("/assets", "/assets")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData(null, "/")]
    public void Sign_in_only_returns_to_giim_pages(string? requested, string expected)
    {
        Assert.Equal(expected, AuthSetup.SafeReturnUrl(requested));
    }
}
