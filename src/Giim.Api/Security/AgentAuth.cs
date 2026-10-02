using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Giim.Api.Security;

internal enum AgentAuthMode
{
    /// <summary>No agent can connect (the default until one is set up).</summary>
    None,

    /// <summary>A long shared key in the X-GIIM-Agent-Key header. For a developer PC and the stand-in agent.</summary>
    Key,

    /// <summary>
    /// The agent's own Entra app registration, signing in with a certificate on its server (client credentials). GIIM
    /// accepts tokens for its API that carry the Giim.Agent app role, from the agent app(s) listed. Use this in Azure.
    /// </summary>
    Entra,
}

internal sealed class AgentAuthOptions
{
    public const string SectionName = "Agent";
    public const int MinimumKeyLength = 32;

    public AgentAuthMode Auth { get; set; } = AgentAuthMode.None;
    public string? Key { get; set; }

    /// <summary>Entra: the agent app registrations (client IDs) allowed in.</summary>
    public string[] AllowedClientIds { get; set; } = [];
}

/// <summary>
/// How the on-prem agent signs in to GIIM. Kept apart from staff sign-in: the agent's credentials only reach the
/// /agent endpoints, and a staff session can't call them.
/// </summary>
internal static class AgentAuth
{
    public const string Scheme = "Agent";
    public const string Policy = "Agent";
    public const string Role = "Giim.Agent";
    public const string KeyHeader = "X-GIIM-Agent-Key";
    private const string KeyScheme = "AgentKey";
    private const string EntraScheme = "AgentEntra";

    public static WebApplicationBuilder AddGiimAgentAuth(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(AgentAuthOptions.SectionName);
        builder.Services.Configure<AgentAuthOptions>(section);
        var agent = section.Get<AgentAuthOptions>() ?? new AgentAuthOptions();
        var entra = builder.Configuration.GetSection($"{AuthOptions.SectionName}:Entra").Get<EntraOptions>() ?? new EntraOptions();

        if (agent.Auth == AgentAuthMode.Key && (agent.Key?.Length ?? 0) < AgentAuthOptions.MinimumKeyLength)
            throw new InvalidOperationException($"Agent:Key must be at least {AgentAuthOptions.MinimumKeyLength} characters.");
        if (agent.Auth == AgentAuthMode.Entra && (!entra.IsConfigured || agent.AllowedClientIds.Length == 0))
            throw new InvalidOperationException("Agent:Auth 'Entra' needs Auth:Entra:TenantId and ClientId, and Agent:AllowedClientIds.");

        var auth = builder.Services.AddAuthentication()
            .AddPolicyScheme(Scheme, "On-prem agent", o => o.ForwardDefaultSelector = _ => agent.Auth == AgentAuthMode.Entra ? EntraScheme : KeyScheme)
            .AddScheme<AuthenticationSchemeOptions, AgentKeyHandler>(KeyScheme, null);
        if (agent.Auth == AgentAuthMode.Entra)
            auth.AddJwtBearer(EntraScheme, o =>
            {
                o.Authority = entra.Authority;
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidAudiences = [entra.ClientId!, $"api://{entra.ClientId}"];
                // Tokens from either endpoint version of this tenant only.
                o.TokenValidationParameters.ValidIssuers = [entra.Authority.TrimEnd('/'), $"https://sts.windows.net/{entra.TenantId}/"];
                o.TokenValidationParameters.RoleClaimType = "roles";
            });

        builder.Services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .AddAuthenticationSchemes(Scheme)
            .RequireRole(Role)
            .RequireAssertion(c => agent.Auth != AgentAuthMode.Entra
                || agent.AllowedClientIds.Contains(c.User.FindFirst("azp")?.Value ?? c.User.FindFirst("appid")?.Value, StringComparer.OrdinalIgnoreCase)));
        return builder;
    }

    /// <summary>Checks the shared key (compared in constant time) and signs the agent in with the agent role.</summary>
    private sealed class AgentKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions, ILoggerFactory logger, UrlEncoder encoder,
        IOptions<AgentAuthOptions> agent) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var o = agent.Value;
            if (o.Auth != AgentAuthMode.Key || string.IsNullOrEmpty(o.Key)) return Task.FromResult(AuthenticateResult.NoResult());
            if (!Request.Headers.TryGetValue(KeyHeader, out var given) || given.Count != 1)
                return Task.FromResult(AuthenticateResult.NoResult());

            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(o.Key));
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(given.ToString()));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return Task.FromResult(AuthenticateResult.Fail("Wrong agent key."));

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "agent (key)"), new Claim(ClaimTypes.Role, Role)], KeyScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), KeyScheme)));
        }
    }
}
