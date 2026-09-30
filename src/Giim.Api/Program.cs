using System.Text.Json.Serialization;
using Giim.Api.Endpoints;
using Giim.Api.Hosting;
using Giim.Api.Security;
using Giim.Connectors;
using Giim.Infrastructure;
using Giim.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddGiimConnectors(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddHealthChecks().AddDbContextCheck<GiimDbContext>();
builder.AddGiimAuth();
builder.AddGiimTelemetry();

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

// Anonymous: health probes, sign-in and sign-in configuration. /health/live only says the app is running (Azure
// restarts an instance that fails it); /health also checks the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health");
app.MapAuthEndpoints();

// Everything else needs a GIIM role; changes need Technician (see AuthSetup.MapSecuredApi).
var api = app.MapSecuredApi();
api.MapAssetEndpoints();
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

// Unknown /api addresses get 404; every other address gets the web UI (built into wwwroot when published).
app.MapUserInterfaceFallback(api);

app.Run();

/// <summary>Exposed so integration tests can host the API.</summary>
public partial class Program;
