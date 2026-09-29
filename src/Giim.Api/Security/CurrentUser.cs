namespace Giim.Api.Security;

/// <summary>The person performing the current request; recorded on every timeline event and audit entry.</summary>
internal interface ICurrentUser
{
    string Name { get; }

    /// <summary>Their full name for display, e.g. "Emily Carter"; the login if Okta didn't send one.</summary>
    string DisplayName { get; }
    string? Email { get; }
    bool IsInRole(string role);
}

/// <summary>
/// The signed-in user's login (their Okta username, e.g. john.smith@company.com.au): unique and stable, so it
/// suits an audit trail. Every endpoint that changes data requires sign-in, so the fallback is never recorded.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public string Name => http.HttpContext?.User.Identity?.Name is { Length: > 0 } name ? name : "unknown";
    public string DisplayName => http.HttpContext?.User.FindFirst("name")?.Value is { Length: > 0 } name ? name : Name;
    public string? Email => http.HttpContext?.User.FindFirst("email")?.Value;
    public bool IsInRole(string role) => http.HttpContext?.User.IsInRole(role) == true;
}
