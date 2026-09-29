namespace Giim.Api.Security;

internal enum AuthMode
{
    /// <summary>Staff sign in with Okta (OpenID Connect). The only mode allowed outside Development.</summary>
    Okta,

    /// <summary>Pick a name and role on a sign-in form. For local development and tests only.</summary>
    Development,
}

internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Okta;

    public OktaOptions Okta { get; set; } = new();

    /// <summary>Okta group that grants each role. Members of several groups get every matching role.</summary>
    public Dictionary<string, string> RoleGroups { get; set; } = new()
    {
        [Roles.Administrator] = "GIIM-Administrators",
        [Roles.Technician] = "GIIM-Technicians",
        [Roles.Manager] = "GIIM-Managers",
        [Roles.Viewer] = "GIIM-Viewers",
    };

    /// <summary>How long a sign-in lasts without activity.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);
}

internal sealed class OktaOptions
{
    /// <summary>Okta authorization server, e.g. https://company.okta.com/oauth2/default</summary>
    public string? Authority { get; set; }
    public string? ClientId { get; set; }

    /// <summary>From user-secrets locally or Key Vault in Azure; never in appsettings.</summary>
    public string? ClientSecret { get; set; }
}
