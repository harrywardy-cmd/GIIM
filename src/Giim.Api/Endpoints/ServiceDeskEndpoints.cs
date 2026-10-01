using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Giim.Api.Security;
using Giim.Connectors.ServiceDesk;
using Giim.Domain.Common;
using Giim.Domain.Integration;
using Giim.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Api.Endpoints;

/// <summary>
/// The ServiceDesk Plus webhook (tickets in) and the admin view of the connection. The webhook is the only address
/// GIIM accepts without a sign-in: it needs the shared secret, is rate-limited, reads only a ticket ID, and the ticket
/// itself is fetched from ServiceDesk Plus later, so a forged call can't create anything.
/// </summary>
internal static partial class ServiceDeskEndpoints
{
    public const string SecretHeader = "X-GIIM-Webhook-Secret";
    public const string RateLimitPolicy = "servicedesk-webhook";
    private const int MaxBodyBytes = 16 * 1024;

    [GeneratedRegex(@"^\d{1,30}$")]
    private static partial Regex RequestKey();

    public static void MapServiceDeskWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/integrations/servicedesk/webhook", async (HttpRequest request, IOptions<ServiceDeskOptions> options, GiimDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var o = options.Value;
            if (o.Mode == ServiceDeskMode.None || string.IsNullOrEmpty(o.WebhookSecret)) return Results.NotFound();
            if (!request.Headers.TryGetValue(SecretHeader, out var given) || !SecretMatches(given.ToString(), o.WebhookSecret))
                return Results.Unauthorized();
            if (request.ContentLength > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            // Also when the sender doesn't say how big the body is: the server stops reading past the limit.
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = MaxBodyBytes;

            string? key;
            try
            {
                using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                key = FindRequestId(json.RootElement);
            }
            catch (JsonException)
            {
                return Results.Problem("The body should be JSON like {\"requestId\": \"123456789\"}.", statusCode: 400);
            }
            catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            if (key is null || !RequestKey().IsMatch(key))
                return Results.Problem("The body needs the ticket's ID, e.g. {\"requestId\": \"123456789\"}.", statusCode: 400);

            // ServiceDesk Plus may send the same ticket twice; one waiting entry is enough.
            if (!await db.ServiceDeskInbound.AnyAsync(e => e.RequestKey == key && e.Status == InboundStatus.Pending, ct))
            {
                db.ServiceDeskInbound.Add(ServiceDeskInboundEvent.Receive(key, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
            }
            return Results.Accepted();
        })
        .AllowAnonymous()
        .DisableAntiforgery()
        .RequireRateLimiting(RateLimitPolicy);
    }

    public static void MapServiceDeskEndpoints(this IEndpointRouteBuilder app)
    {
        // Administrators only, reading included (changes are also on the AuthSetup.AdministratorOnly list).
        var group = app.MapGroup("/api/integrations/servicedesk").RequireAuthorization(Policies.Administer);

        // What is connected and what happened recently. Never returns secrets.
        group.MapGet("", async (IOptions<ServiceDeskOptions> options, IConfiguration config, GiimDbContext db, CancellationToken ct) =>
        {
            var o = options.Value;
            var inbound = await db.ServiceDeskInbound.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(50)
                .Select(e => new { e.Id, e.RequestKey, e.DisplayId, e.Kind, e.Status, e.Message, e.CaseId, e.Attempts, e.CreatedAt, e.UpdatedAt })
                .ToListAsync(ct);
            var updates = await db.ServiceDeskUpdates.AsNoTracking().OrderByDescending(u => u.CreatedAt).Take(50)
                .Select(u => new { u.Id, u.DisplayId, u.Kind, u.Status, u.Content, u.CaseId, u.DeviceRequestId, u.Attempts, u.CreatedAt, u.SentAt, u.LastError })
                .ToListAsync(ct);
            var baseUrl = config["Giim:PublicBaseUrl"];
            return Results.Ok(new
            {
                Mode = o.Mode.ToString(),
                WebhookEnabled = o.Mode != ServiceDeskMode.None && !string.IsNullOrEmpty(o.WebhookSecret),
                WebhookUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/integrations/servicedesk/webhook",
                Site = new Uri(o.SiteUrl, $"app/{o.Portal}/").ToString(),
                o.AutoRaiseDeviceRequests,
                o.ResolveWhenComplete,
                Starter = new { o.Starter.Templates, o.Starter.Fields },
                Leaver = new { o.Leaver.Templates, o.Leaver.Fields },
                Inbound = inbound,
                Updates = updates,
            });
        });

        // After fixing what went wrong (e.g. adding the department), process the ticket again.
        group.MapPost("/inbound/{id:guid}/retry", async (Guid id, GiimDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var inbound = await db.ServiceDeskInbound.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (inbound is null) return Results.NotFound();
            try
            {
                inbound.Requeue(clock.GetUtcNow());
            }
            catch (DomainException e)
            {
                return Results.Problem(e.Message, statusCode: 400);
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Accepts {"requestId": …}, {"request_id": …}, {"id": …} or {"request": {"id": …}}, as text or a number.</summary>
    private static string? FindRequestId(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "requestId", "request_id", "id" })
            if (body.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString()?.Trim();
        return body.TryGetProperty("request", out var request) ? FindRequestId(request) : null;
    }

    /// <summary>Constant-time comparison, so the secret can't be guessed from how long a wrong one takes to reject.</summary>
    private static bool SecretMatches(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
