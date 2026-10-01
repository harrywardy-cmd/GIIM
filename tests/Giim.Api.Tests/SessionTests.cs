using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Giim.Api.Tests;

/// <summary>A sign-in ends a fixed time after it started, however active it is, so removed access doesn't linger.</summary>
public class SessionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The app's clock, moved forward by the test.</summary>
    private sealed class MovableTime : TimeProvider
    {
        public TimeSpan Ahead { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Ahead;
    }

    [Fact]
    public async Task Session_ends_after_the_maximum_lifetime_even_while_in_use()
    {
        var time = new MovableTime();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(time)));
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/auth/dev-login", new { name = "test.technician", role = "Technician" });

        // Busy all day: a request every few hours keeps the 8-hour idle limit from ending the session...
        foreach (var hours in new[] { 3, 6, 9 })
        {
            time.Ahead = TimeSpan.FromHours(hours);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        }

        // ...but 10 hours after signing in it ends anyway.
        time.Ahead = TimeSpan.FromHours(10) + TimeSpan.FromMinutes(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }
}
