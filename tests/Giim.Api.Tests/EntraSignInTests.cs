using System.Net;
using System.Net.Http.Json;
using Giim.Api.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Giim.Api.Tests;

/// <summary>
/// Sign-in with Microsoft Entra ID, checked without contacting Microsoft: how the sign-in is configured, and how GIIM
/// behaves before the app registration exists.
/// </summary>
public class EntraSignInTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string Client = "66666666-7777-8888-9999-000000000000";

    private WebApplicationFactory<Program> Entra(string? tenant = Tenant, string? client = Client) =>
        factory.WithWebHostBuilder(b => b
            .UseSetting("Auth:Mode", "Entra")
            .UseSetting("Auth:Entra:TenantId", tenant ?? "")
            .UseSetting("Auth:Entra:ClientId", client ?? "")
            .UseSetting("Auth:Entra:ManagedIdentityClientId", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    [Fact]
    public void Signs_in_against_this_organisations_directory_with_roles_from_entra_app_roles()
    {
        using var app = Entra();
        _ = app.Server;

        var oidc = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/v2.0", oidc.Authority);   // single tenant, not "common"
        Assert.Equal(Client, oidc.ClientId);
        Assert.Equal("code", oidc.ResponseType);
        Assert.True(oidc.UsePkce);
        Assert.Equal("roles", oidc.TokenValidationParameters.RoleClaimType);
        Assert.Equal("preferred_username", oidc.TokenValidationParameters.NameClaimType);
        Assert.Equal(["email", "openid", "profile"], oidc.Scope.Order(StringComparer.Ordinal));
        Assert.Null(oidc.ClientSecret);   // in Azure the managed identity proves who GIIM is; no secret
    }

    [Fact]
    public async Task Before_the_app_registration_exists_giim_runs_and_says_sign_in_is_not_set_up()
    {
        using var app = Entra(tenant: null, client: null);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var config = await client.GetFromJsonAsync<AuthConfig>("/api/auth/config");
        var login = await client.GetAsync("/auth/login");

        Assert.Equal("Entra", config!.Mode);
        Assert.False(config.Configured);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.ToString());   // back to the page that explains, not an error
    }

    [Fact]
    public async Task Production_starts_with_microsoft_sign_in_not_yet_configured()
    {
        using var production = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Giim", "Server=unused")
            .UseSetting("Auth:Mode", "Entra"));

        var response = await production.CreateClient().GetAsync("/api/auth/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_sign_in_does_not_exist_with_microsoft_sign_in()
    {
        using var app = Entra();

        var response = await app.CreateClient().PostAsJsonAsync("/auth/dev-login", new { name = "x", role = Roles.Administrator });

        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record AuthConfig(string Mode, bool Configured, string[] Roles);
}
