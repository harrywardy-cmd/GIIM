namespace Giim.Api.Security;

/// <summary>The person performing the current request; recorded on every timeline event and audit entry.</summary>
internal interface ICurrentUser
{
    string Name { get; }
}

/// <summary>
/// The signed-in user's login (their Okta username, e.g. john.smith@company.com.au): unique and stable, so it
/// suits an audit trail. Every endpoint that changes data requires sign-in, so the fallback is never recorded.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public string Name => http.HttpContext?.User.Identity?.Name is { Length: > 0 } name ? name : "unknown";
}
