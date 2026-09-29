using Azure.Monitor.OpenTelemetry.AspNetCore;
using Giim.Connectors;
using Giim.Infrastructure;
using Giim.Infrastructure.Persistence;
using Giim.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// A web host only so Azure App Service can check the workers are alive (/health); the real work is the
// background services below. It has no other endpoints and no public access in Azure.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddGiimConnectors(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<GiimDbContext>();
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddHostedService<IntuneSyncWorker>();

var app = builder.Build();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health");
app.Run();
