using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Net.Http.Headers;

namespace Giim.Api.Hosting;

/// <summary>
/// What the API needs to run in Azure: telemetry, shared sign-in keys, security headers and serving the web UI.
/// Everything here switches on from configuration, so a developer PC runs without any of it.
/// </summary>
internal static class WebHosting
{
    /// <summary>Sends requests, dependencies, logs and exceptions to Application Insights when it's configured.</summary>
    public static WebApplicationBuilder AddGiimTelemetry(this WebApplicationBuilder builder)
    {
        if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        return builder;
    }

    /// <summary>
    /// Keys that encrypt the sign-in cookie. In Azure they live in Blob Storage, wrapped by a Key Vault key, so every
    /// API instance shares them and sign-ins survive restarts and deployments. Locally the default key store is used.
    /// </summary>
    public static IDataProtectionBuilder StoreKeys(this IDataProtectionBuilder dataProtection, IConfiguration configuration)
    {
        var blob = configuration["DataProtection:BlobUri"];
        var key = configuration["DataProtection:KeyUri"];
        if (string.IsNullOrEmpty(blob) && string.IsNullOrEmpty(key)) return dataProtection;
        if (string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Set both DataProtection:BlobUri and DataProtection:KeyUri, or neither.");

        // In Azure, AZURE_TOKEN_CREDENTIALS and AZURE_CLIENT_ID pin this to the app's managed identity.
        var credential = new DefaultAzureCredential();
        return dataProtection
            .PersistKeysToAzureBlobStorage(new Uri(blob), credential)
            .ProtectKeysWithAzureKeyVault(new Uri(key), credential);
    }

    /// <summary>
    /// Browser security headers on every response. The UI loads only its own scripts; the one outside address it
    /// needs is Okta, because signing out is a form post that redirects there.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        var okta = Uri.TryCreate(app.Configuration["Auth:Okta:Authority"], UriKind.Absolute, out var authority)
            ? " " + authority.GetLeftPart(UriPartial.Authority)
            : "";
        var policy = string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline'",     // React style props and the inline QR SVGs on label sheets
            "img-src 'self' data: blob:",
            "font-src 'self'",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'",
            $"form-action 'self'{okta}");

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers[HeaderNames.ContentSecurityPolicy] = policy;
            await next();
        });
    }

    /// <summary>
    /// Serves the built React app from wwwroot. File names under /assets contain a content hash, so browsers may
    /// cache them for a year; index.html is always re-checked so a deployment shows up straight away.
    /// </summary>
    public static IApplicationBuilder UseUserInterface(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => SetCaching(context.Context) });
        return app;
    }

    /// <summary>Unknown /api addresses get 404; anything else gets the app, which shows its own sign-in page.</summary>
    public static void MapUserInterfaceFallback(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapFallback("/api/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html", new StaticFileOptions { OnPrepareResponse = context => SetCaching(context.Context) });
    }

    private static void SetCaching(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
}
