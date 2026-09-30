using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Giim.Api.Tests;

/// <summary>
/// The ServiceDesk Plus webhook is the one address open without a sign-in. Each of these is refused before anything
/// touches the database, so no database is needed.
/// </summary>
public class ServiceDeskWebhookTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/integrations/servicedesk/webhook";
    private const string DevSecret = "dev-webhook-secret-only-works-on-a-developer-pc";   // appsettings.Development.json

    private static HttpRequestMessage Post(string body, string? secret)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (secret is not null) message.Headers.Add("X-GIIM-Webhook-Secret", secret);
        return message;
    }

    [Fact]
    public async Task Without_the_secret_the_webhook_refuses()
    {
        using var request = Post("""{"requestId": "123"}""", secret: null);
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task With_the_wrong_secret_the_webhook_refuses()
    {
        using var request = Post("""{"requestId": "123"}""", secret: "guess");
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"subject": "no id"}""")]
    [InlineData("""{"requestId": "1; DROP TABLE Cases"}""")]
    public async Task Only_a_ticket_id_is_accepted(string body)
    {
        using var request = Post(body, DevSecret);
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Large_bodies_are_refused()
    {
        using var request = Post("{\"requestId\": \"1\", \"padding\": \"" + new string('x', 20_000) + "\"}", DevSecret);
        var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task When_servicedesk_is_not_connected_the_webhook_does_not_exist()
    {
        using var off = factory.WithWebHostBuilder(b => b.UseSetting("ServiceDesk:Mode", "None"));
        using var request = Post("""{"requestId": "123"}""", DevSecret);
        var response = await off.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
