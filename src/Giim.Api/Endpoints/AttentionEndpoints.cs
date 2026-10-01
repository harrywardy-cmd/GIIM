using Giim.Api.Security;
using Giim.Infrastructure.Attention;
using Giim.Infrastructure.Requests;

namespace Giim.Api.Endpoints;

/// <summary>The bell: what needs the signed-in person now, for their roles.</summary>
internal static class AttentionEndpoints
{
    public static void MapAttentionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attention", async (AttentionService attention, RequestService requests, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await attention.GetAsync(await RequestEndpoints.ActorAsync(requests, user, ct), ct)));
    }
}
