using System.Net;
using System.Net.Http.Json;
using Giim.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Giim.Api.Tests;

/// <summary>Browser security headers and the split between API addresses and the web UI. No database needed.</summary>
public class HostingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/config");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_api_address_needs_sign_in_then_is_not_found()
    {
        var anonymous = await factory.CreateClient().GetAsync("/api/no-such-thing");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/auth/dev-login", new { name = "test.viewer", role = "Viewer" });
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "1");

        var signedIn = await client.GetAsync("/api/no-such-thing");

        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);   // never the web page, which would confuse API callers
    }

    [Fact]
    public async Task Sign_out_may_redirect_to_okta_and_nowhere_else()
    {
        using var okta = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Okta:Authority", "https://example.okta.com/oauth2/default"));

        var response = await okta.CreateClient().GetAsync("/api/auth/config");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("form-action 'self' https://example.okta.com;", policy + ";", StringComparison.Ordinal);
    }
}
