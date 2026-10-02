using Azure.Monitor.OpenTelemetry.AspNetCore;
using Giim.Connectors;
using Giim.Connectors.Email;
using Giim.Connectors.People;
using Giim.Connectors.ServiceDesk;
using Giim.Infrastructure;
using Giim.Infrastructure.Automation;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.ServiceDesk;
using Giim.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// A web host only so Azure App Service can check the workers are alive (/health); the real work is the
// background services below. It has no other endpoints and no public access in Azure.
var builder = WebApplication.CreateBuilder(args);

// ServiceDesk Plus field mapping (config/servicedesk.json, shared by the API and the workers). Lowest priority, so
// app settings and environment variables can still override any of it.
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "servicedesk.json",
    Optional = true,
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory),
});

builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddGiimConnectors(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<GiimDbContext>();
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddHostedService<IntuneSyncWorker>();
// Staff directory from Entra ID on a schedule. A CSV source (developer PC) is only synced on request.
if (builder.Configuration.GetValue<PeopleSource?>("People:Source") == PeopleSource.Entra)
    builder.Services.AddHostedService<PeopleSyncWorker>();
// Sends queued emails; only the workers send (the API just queues them in the same save as the change).
if (builder.Configuration.GetValue<EmailMode?>("Email:Mode") is EmailMode.File or EmailMode.Graph)
    builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationWorker>();

builder.Services.Configure<GiimOptions>(builder.Configuration.GetSection("Giim"));
builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection(AutomationOptions.SectionName));
// GIIM's own automation steps (cloud-sync wait, welcome email); the on-prem agent does the AD and Exchange ones.
builder.Services.AddHostedService<AutomationWorker>();
// Dashboard totals once an hour, for the week-on-week arrows.
builder.Services.AddHostedService<SnapshotWorker>();
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection(ReminderOptions.SectionName));
// Manager emails, reminders and digests (Reminders:Enabled, on unless switched off).
if (builder.Configuration.GetValue<bool?>("Reminders:Enabled") ?? true)
    builder.Services.AddHostedService<ReminderWorker>();

// ServiceDesk Plus: tickets in, notes out. Off unless ServiceDesk:Mode is File or Api.
if (builder.Configuration.GetValue<ServiceDeskMode?>("ServiceDesk:Mode") is ServiceDeskMode.File or ServiceDeskMode.Api)
{
    builder.Services.AddScoped<ServiceDeskSync>();
    builder.Services.AddHostedService<ServiceDeskWorker>();
}

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health");
app.Run();
