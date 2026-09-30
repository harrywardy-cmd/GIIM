namespace Giim.Api.Security;

internal enum AuthMode
{
    /// <summary>
    /// Staff sign in with Microsoft Entra ID (OpenID Connect), usually from the GIIM tile in My Apps. The only mode
    /// allowed outside Development.
    /// </summary>
    Entra,

    /// <summary>Pick a name and role on a sign-in form. For local development and tests only.</summary>
    Development,
}

internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Entra;

    public EntraOptions Entra { get; set; } = new();

    /// <summary>How long a sign-in lasts without activity.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);
}

/// <summary>The GIIM app registration in Microsoft Entra ID (see docs/entra-setup.md).</summary>
internal sealed class EntraOptions
{
    /// <summary>The Microsoft sign-in service; only changes for sovereign clouds.</summary>
    public Uri Instance { get; set; } = new("https://login.microsoftonline.com/");

    /// <summary>Your organisation's Entra tenant (directory) ID.</summary>
    public string? TenantId { get; set; }

    /// <summary>The GIIM app registration's application (client) ID.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// In Azure: the client ID of GIIM's managed identity, which the app registration trusts (a federated
    /// credential). GIIM then proves who it is with a short-lived token from Azure, so there is no secret to store
    /// or renew.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Only for testing against real Entra from a developer PC (user-secrets); never in Azure.</summary>
    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>Single-tenant: only accounts from this organisation's directory can sign in.</summary>
    public string Authority => new Uri(Instance, $"{TenantId}/v2.0").ToString();
}
