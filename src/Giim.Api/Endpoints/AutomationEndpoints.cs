using System.Text.Json;
using Giim.Api.Security;
using Giim.Domain.Automation;
using Giim.Domain.Common;
using Giim.Infrastructure.Automation;

namespace Giim.Api.Endpoints;

internal sealed record AgentCheckInRequest(string Name, string? Version, string? Directory, bool DryRun);
internal sealed record AgentClaimRequest(string Name, int Max = 5);
internal sealed record AgentResultRequest(string Name, bool Succeeded, JsonElement? Result, string? Error, string? Log, bool Retryable);

/// <summary>
/// Starter checklist automation: technicians start, retry and stop it (under /api, staff sign-in); the on-prem agent
/// checks in, claims jobs and reports results (under /agent, the agent's own sign-in).
/// </summary>
internal static class AutomationEndpoints
{
    /// <summary>How often the agent should ask for work, in seconds.</summary>
    private const int PollSeconds = 15;

    public static void MapAutomationEndpoints(this IEndpointRouteBuilder api)
    {
        var cases = api.MapGroup("/api/cases/{caseId:guid}/automation");

        cases.MapGet("", (Guid caseId, AutomationService automation, CancellationToken ct) => Handle(async () =>
            Results.Ok(await automation.GetAsync(caseId, ct))));

        cases.MapPost("/start", (Guid caseId, AutomationService automation, ICurrentUser user, CancellationToken ct) => Handle(async () =>
            Results.Ok(new { started = await automation.StartAsync(caseId, user.Name, ct) })));

        cases.MapPost("/jobs/{jobId:guid}/retry", (Guid caseId, Guid jobId, AutomationService automation, ICurrentUser user, CancellationToken ct) =>
            Handle(async () =>
            {
                await automation.RetryAsync(caseId, jobId, user.Name, ct);
                return Results.NoContent();
            }));

        cases.MapPost("/jobs/{jobId:guid}/stop", (Guid caseId, Guid jobId, AutomationService automation, ICurrentUser user, CancellationToken ct) =>
            Handle(async () =>
            {
                await automation.StopAsync(caseId, jobId, user.Name, ct);
                return Results.NoContent();
            }));

        api.MapGet("/api/automation", async (AutomationService automation, CancellationToken ct) => Results.Ok(await automation.OverviewAsync(ct)));
    }

    public static void MapAgentEndpoints(this WebApplication app)
    {
        var agent = app.MapGroup("/agent").RequireAuthorization(AgentAuth.Policy).RequireRateLimiting("agent");

        agent.MapPost("/check-in", (AgentCheckInRequest request, AutomationService automation, Microsoft.Extensions.Options.IOptions<AutomationOptions> options,
            CancellationToken ct) => Handle(async () =>
        {
            await automation.CheckInAsync(request.Name, request.Version, request.Directory, request.DryRun, ct);
            // GIIM's dry-run setting is the one that counts; the agent shows it in its log.
            return Results.Ok(new { pollSeconds = PollSeconds, dryRun = options.Value.DryRun });
        }));

        agent.MapPost("/jobs/claim", (AgentClaimRequest request, AutomationService automation, CancellationToken ct) => Handle(async () =>
            Results.Ok(await automation.ClaimAsync(request.Name, AutomationRunner.Agent, request.Max, ct))));

        agent.MapPost("/jobs/{jobId:guid}/result", (Guid jobId, AgentResultRequest request, AutomationService automation, CancellationToken ct) =>
            Handle(async () =>
            {
                var result = request.Result is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } r ? r.GetRawText() : null;
                await automation.CompleteAsync(jobId, request.Name, new JobResult(request.Succeeded, result, request.Error, request.Log, request.Retryable), ct);
                return Results.NoContent();
            }));
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (DomainException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
