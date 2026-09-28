using Giim.Connectors;
using Giim.Infrastructure;
using Giim.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));
builder.Services.AddGiimConnectors(builder.Configuration);

builder.Services.AddHostedService<IntuneSyncWorker>();

var host = builder.Build();
host.Run();
