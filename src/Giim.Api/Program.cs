using System.Text.Json.Serialization;
using Giim.Api.Endpoints;
using Giim.Api.Hosting;
using Giim.Api.Security;
using Giim.Connectors;
using Giim.Infrastructure;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ServiceDesk Plus field mapping (config/servicedesk.json, shared by the API and the workers). Lowest priority, so
// app settings and environment variables can still override any of it.
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "servicedesk.json",
    Optional = true,
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory),
});

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddGiimConnectors(builder.Configuration);
builder.Services.AddGiimAttachments(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddHealthChecks().AddDbContextCheck<GiimDbContext>();
builder.AddGiimAuth();
builder.AddGiimTelemetry();
builder.Services.Configure<GiimOptions>(builder.Configuration.GetSection("Giim"));
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection(ReminderOptions.SectionName));

// The ServiceDesk Plus webhook is the one address open without a sign-in, so it is rate-limited as well as secret-checked.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter(ServiceDeskEndpoints.RateLimitPolicy, w =>
    {
        w.PermitLimit = 120;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
    app.UseHsts();   // browsers remember to use HTTPS only

app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseUserInterface();
app.UseAuthentication();
app.UseAuthorization();
app.UseCsrfHeaderCheck();
app.UseRateLimiter();

// Anonymous: health probes, sign-in, sign-in configuration and the ServiceDesk Plus webhook (secret-checked). /health/live only says the app is running (Azure
// restarts an instance that fails it); /health also checks the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapServiceDeskWebhook();

// Everything else needs a GIIM role; changes need Technician (see AuthSetup.MapSecuredApi).
var api = app.MapSecuredApi();
api.MapAssetEndpoints();
api.MapAttachmentEndpoints();
api.MapImportEndpoints();
api.MapCategoryEndpoints();
api.MapStockEndpoints();
api.MapIntuneEndpoints();
api.MapPeopleEndpoints();
api.MapDashboardEndpoints();
api.MapLabelEndpoints();
api.MapLocationEndpoints();
api.MapActivityEndpoints();
api.MapReportEndpoints();
api.MapRequestEndpoints();
api.MapCaseEndpoints();
api.MapServiceDeskEndpoints();
api.MapNotificationEndpoints();
api.MapAttentionEndpoints();

// Unknown /api addresses get 404; every other address gets the web UI (built into wwwroot when published).
app.MapUserInterfaceFallback(api);

app.Run();

/// <summary>Exposed so integration tests can host the API.</summary>
public partial class Program;
