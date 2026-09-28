using System.Text.Json.Serialization;
using Giim.Api.Endpoints;
using Giim.Api.Security;
using Giim.Connectors;
using Giim.Infrastructure;
using Giim.Infrastructure.Persistence;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");

app.MapAssetEndpoints();
app.MapImportEndpoints();
app.MapCategoryEndpoints();
app.MapStockEndpoints();
app.MapIntuneEndpoints();
app.MapPeopleEndpoints();
app.MapDashboardEndpoints();
app.MapLabelEndpoints();
app.MapLocationEndpoints();

app.Run();
