using System.Security.Claims;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Giim.Api.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Giim.Api.Security;

internal sealed record DevSignInRequest(string Name, string Role);

/// <summary>
/// Sign-in for GIIM. The API does the OpenID Connect sign-in with Microsoft Entra ID (staff usually start from the
/// GIIM tile in My Apps) and keeps the session in an HTTP-only cookie, so no tokens ever reach the browser. API calls
/// without a session get 401 (the UI then shows the sign-in page) rather than a redirect.
/// </summary>
internal static class AuthSetup
{
    public const string CsrfHeader = "X-GIIM-Request";

    /// <summary>The claim Entra puts the user's app roles in (Administrator, Technician, Manager, Viewer).</summary>
    public const string RolesClaim = "roles";

    /// <summary>When the person signed in (Unix seconds); kept in the session so its total length can be limited.</summary>
    public const string SignedInClaim = "giim_signed_in";

    /// <summary>What a managed identity's token must be issued for when used as the app registration's credential.</summary>
    private const string FederatedCredentialScope = "api://AzureADTokenExchange/.default";

    private static readonly string[] Scopes = ["openid", "profile", "email"];

    public static WebApplicationBuilder AddGiimAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

        if (options.Mode == AuthMode.Development && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Auth:Mode 'Development' is only allowed in the Development environment. Use 'Entra' everywhere else.");

        builder.Services.AddDataProtection().SetApplicationName("GIIM").StoreKeys(builder.Configuration);

        var auth = builder.Services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = "giim.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // HTTPS-only everywhere except a developer PC (the Vite dev server is plain http).
                o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = options.SessionIdleTimeout;
                o.SlidingExpiration = true;
                // The UI handles sign-in; API calls get status codes, not redirects.
                o.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                // Sliding sessions would otherwise last for ever while in use; end them a fixed time after sign-in.
                o.Events.OnValidatePrincipal = async context =>
                {
                    var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    var signedIn = long.TryParse(context.Principal?.FindFirstValue(SignedInClaim), out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
                    if (signedIn is null || now - signedIn.Value > options.MaxSessionLifetime)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        // Until the app registration exists GIIM still runs; the sign-in page says sign-in isn't set up yet.
        if (options.Mode == AuthMode.Entra && options.Entra.IsConfigured)
            auth.AddOpenIdConnect(o => ConfigureEntra(o, options.Entra));

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Read, p => p.RequireRole(Roles.All))
            .AddPolicy(Policies.Change, p => p.RequireRole(Roles.Technician, Roles.Administrator))
            .AddPolicy(Policies.Request, p => p.RequireRole(Roles.Manager, Roles.Technician, Roles.Administrator))
            .AddPolicy(Policies.Administer, p => p.RequireRole(Roles.Administrator));

        return builder;
    }

    /// <summary>OpenID Connect with Microsoft Entra ID. Roles are the app roles assigned to the user (or their groups) in Entra.</summary>
    private static void ConfigureEntra(OpenIdConnectOptions o, EntraOptions entra)
    {
        o.Authority = entra.Authority;
        o.ClientId = entra.ClientId;
        o.ClientSecret = entra.ClientSecret;
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.SaveTokens = true;                        // the ID token lets sign-out end the Microsoft session too
        o.GetClaimsFromUserInfoEndpoint = false;    // everything GIIM needs is in the ID token
        o.MapInboundClaims = false;
        o.Scope.Clear();
        foreach (var scope in Scopes) o.Scope.Add(scope);
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.TokenValidationParameters.RoleClaimType = RolesClaim;
        o.Events.OnTokenValidated = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity identity) identity.AddClaim(SignedInNow(context.HttpContext));
            return Task.CompletedTask;
        };

        // In Azure GIIM proves who it is with a short-lived token from its managed identity, which the app
        // registration trusts (a federated credential), instead of a stored client secret.
        if (!string.IsNullOrWhiteSpace(entra.ManagedIdentityClientId))
        {
            var identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entra.ManagedIdentityClientId));
            o.Events.OnAuthorizationCodeReceived = async context =>
            {
                var assertion = await identity.GetTokenAsync(new TokenRequestContext([FederatedCredentialScope]), context.HttpContext.RequestAborted);
                context.TokenEndpointRequest!.ClientSecret = null;
                context.TokenEndpointRequest.ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
                context.TokenEndpointRequest.ClientAssertion = assertion.Token;
            };
        }
    }

    /// <summary>
    /// Every GIIM API endpoint goes through this group: reading needs any GIIM role, and any request that changes
    /// data (POST, PUT, PATCH, DELETE) needs Technician or Administrator, including endpoints added in future.
    /// </summary>
    public static RouteGroupBuilder MapSecuredApi(this WebApplication app)
    {
        var api = app.MapGroup("").RequireAuthorization(Policies.Read);
        // Finally runs after each endpoint's own conventions, so the HTTP method metadata is already there.
        ((IEndpointConventionBuilder)api).Finally(endpoint =>
        {
            var methods = endpoint.Metadata.OfType<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods).ToList();
            if (!methods.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m))) return;

            // Device request actions managers take (raise, approve...) are the one exception, and are marked explicitly.
            endpoint.Metadata.Add(new AuthorizeAttribute(
                endpoint.Metadata.OfType<ManagersAllowed>().Any() ? Policies.Request : Policies.Change));
            var route = (endpoint as RouteEndpointBuilder)?.RoutePattern.RawText ?? "";
            if (AdministratorOnly.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                endpoint.Metadata.Add(new AuthorizeAttribute(Policies.Administer));
        });
        return api;
    }

    /// <summary>Lets managers (as well as technicians and administrators) use this endpoint. For device requests only.</summary>
    public static TBuilder AllowManagers<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ManagersAllowed());

    internal sealed class ManagersAllowed;

    /// <summary>
    /// Changes that shape the whole system rather than one asset: set-up lists, bulk imports and directory sync.
    /// Kept in one list so it's easy to review.
    /// </summary>
    internal static readonly string[] AdministratorOnly =
    [
        "/api/categories",
        "/api/locations",
        "/api/profiles",
        "/api/integrations",
        "/api/notifications",
        "/api/imports",
        "/api/people/sync",
        "/api/people/legacy-owners/link-automatic",
    ];

    /// <summary>
    /// Changes must carry a custom header. Other websites can't add custom headers to requests they trigger, so a
    /// signed-in browser can't be tricked into changing data (cross-site request forgery).
    /// </summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
                && !request.Headers.ContainsKey(CsrfHeader))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { title = "Bad request", detail = $"Missing {CsrfHeader} header." });
                return;
            }
            await next();
        });

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        // Tells the sign-in page which kind of sign-in to offer.
        app.MapGet("/api/auth/config", () => Results.Ok(new
        {
            mode = options.Mode.ToString(),
            configured = options.Mode == AuthMode.Development || options.Entra.IsConfigured,
            roles = Roles.All,
        })).AllowAnonymous();

        // Who is signed in. Any signed-in user may ask, even without a GIIM role, so the UI can explain.
        app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            name = user.FindFirstValue("name") ?? user.Identity?.Name,
            login = user.Identity?.Name,
            email = user.FindFirstValue("email"),
            roles = Roles.All.Where(user.IsInRole).ToArray(),
        })).RequireAuthorization();

        if (options.Mode == AuthMode.Entra)
        {
            // The My Apps tile opens this address, so sign-in starts straight away (and, already signed in to
            // Microsoft, finishes without a prompt). Someone already signed in to GIIM just carries on.
            app.MapGet("/auth/login", (string? returnUrl, HttpContext context) =>
                !options.Entra.IsConfigured ? Results.Redirect("/")
                : context.User.Identity?.IsAuthenticated == true ? Results.Redirect(SafeReturnUrl(returnUrl))
                : Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();

            // Ends the GIIM session and the Microsoft session in this browser, so a shared PC is left signed out.
            app.MapPost("/auth/logout", () => options.Entra.IsConfigured
                ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])
                : Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [CookieAuthenticationDefaults.AuthenticationScheme]))
                .AllowAnonymous();
        }
        else
        {
            // Development only (startup refuses this mode anywhere else): sign in as anyone with any role.
            app.MapPost("/auth/dev-login", async (DevSignInRequest request, HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) || !Roles.All.Contains(request.Role))
                    return Results.Problem("Give a name and one of: " + string.Join(", ", Roles.All), statusCode: 400);

                var login = request.Name.Trim();
                var identity = new ClaimsIdentity(
                    [new Claim("preferred_username", login), new Claim("name", login), new Claim(ClaimTypes.Role, request.Role), SignedInNow(context)],
                    CookieAuthenticationDefaults.AuthenticationScheme, "preferred_username", ClaimTypes.Role);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                return Results.NoContent();
            }).AllowAnonymous();

            app.MapPost("/auth/logout", async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/");
            }).AllowAnonymous();
        }
    }

    private static Claim SignedInNow(HttpContext context) => new(SignedInClaim,
        context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Only same-site paths, so the sign-in can't be used to bounce someone to another website.</summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
