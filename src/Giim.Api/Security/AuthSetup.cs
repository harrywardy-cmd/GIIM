using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Giim.Api.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Giim.Api.Security;

internal sealed record DevSignInRequest(string Name, string Role);

/// <summary>
/// Sign-in for GIIM. The API does the OpenID Connect sign-in with Okta and keeps the session in an HTTP-only
/// cookie, so no tokens ever reach the browser. API calls without a session get 401 (the UI then shows the
/// sign-in page) rather than a redirect.
/// </summary>
internal static class AuthSetup
{
    public const string CsrfHeader = "X-GIIM-Request";

    private static readonly string[] Scopes = ["openid", "profile", "email", "groups"];

    public static WebApplicationBuilder AddGiimAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

        if (options.Mode == AuthMode.Development && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Auth:Mode 'Development' is only allowed in the Development environment. Use 'Okta' everywhere else.");

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
            });

        if (options.Mode == AuthMode.Okta)
        {
            auth.AddOpenIdConnect(o =>
            {
                o.Authority = options.Okta.Authority;
                o.ClientId = options.Okta.ClientId;
                o.ClientSecret = options.Okta.ClientSecret;
                o.ResponseType = "code";
                o.UsePkce = true;
                o.SaveTokens = true;                        // the id token is needed to sign out of Okta as well
                o.GetClaimsFromUserInfoEndpoint = true;     // Okta returns group membership here
                o.MapInboundClaims = false;
                o.Scope.Clear();
                foreach (var scope in Scopes) o.Scope.Add(scope);
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                o.ClaimActions.MapJsonKey(GroupRoleMapper.GroupClaim, GroupRoleMapper.GroupClaim);
                o.Events.OnTokenValidated = context =>
                {
                    var roleGroups = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RoleGroups;
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                        GroupRoleMapper.AddRoleClaims(identity, roleGroups);
                    return Task.CompletedTask;
                };
                o.Events.OnUserInformationReceived = context =>
                {
                    // Groups can arrive from the userinfo endpoint after token validation; map them too.
                    var roleGroups = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.RoleGroups;
                    if (context.Principal?.Identity is ClaimsIdentity identity
                        && context.User.RootElement.TryGetProperty(GroupRoleMapper.GroupClaim, out var groups))
                    {
                        foreach (var group in groups.EnumerateArray())
                            if (group.GetString() is { } name && !identity.HasClaim(GroupRoleMapper.GroupClaim, name))
                                identity.AddClaim(new Claim(GroupRoleMapper.GroupClaim, name));
                        foreach (var role in GroupRoleMapper.RolesFor(identity.FindAll(GroupRoleMapper.GroupClaim).Select(c => c.Value), roleGroups))
                            if (!identity.HasClaim(identity.RoleClaimType, role))
                                identity.AddClaim(new Claim(identity.RoleClaimType, role));
                    }
                    return Task.CompletedTask;
                };
            });
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Read, p => p.RequireRole(Roles.All))
            .AddPolicy(Policies.Change, p => p.RequireRole(Roles.Technician, Roles.Administrator))
            .AddPolicy(Policies.Administer, p => p.RequireRole(Roles.Administrator));

        return builder;
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

            endpoint.Metadata.Add(new AuthorizeAttribute(Policies.Change));
            var route = (endpoint as RouteEndpointBuilder)?.RoutePattern.RawText ?? "";
            if (AdministratorOnly.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                endpoint.Metadata.Add(new AuthorizeAttribute(Policies.Administer));
        });
        return api;
    }

    /// <summary>
    /// Changes that shape the whole system rather than one asset: set-up lists, bulk imports and directory sync.
    /// Kept in one list so it's easy to review.
    /// </summary>
    internal static readonly string[] AdministratorOnly =
    [
        "/api/categories",
        "/api/locations",
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
        app.MapGet("/api/auth/config", () => Results.Ok(new { mode = options.Mode.ToString(), roles = Roles.All })).AllowAnonymous();

        // Who is signed in. Any signed-in user may ask, even without a GIIM role, so the UI can explain.
        app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            name = user.FindFirstValue("name") ?? user.Identity?.Name,
            login = user.Identity?.Name,
            email = user.FindFirstValue("email"),
            roles = Roles.All.Where(user.IsInRole).ToArray(),
        })).RequireAuthorization();

        if (options.Mode == AuthMode.Okta)
        {
            app.MapGet("/auth/login", (string? returnUrl) =>
                Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();

            app.MapPost("/auth/logout", () =>
                Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
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
                    [new Claim("preferred_username", login), new Claim("name", login), new Claim(ClaimTypes.Role, request.Role)],
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

    /// <summary>Only same-site paths, so the sign-in can't be used to bounce someone to another website.</summary>
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
