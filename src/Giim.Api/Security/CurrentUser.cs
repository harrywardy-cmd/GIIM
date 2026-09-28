namespace Giim.Api.Security;

/// <summary>The person performing the current request; recorded on every timeline event and audit entry.</summary>
internal interface ICurrentUser
{
    string Name { get; }
}

/// <summary>
/// Until Okta SSO is added there is no signed-in user, so actions are recorded as "local-dev".
/// When SSO arrives this returns the Okta username and every endpoint picks it up automatically.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public string Name => http.HttpContext?.User.Identity?.Name is { Length: > 0 } name ? name : "local-dev";
}
