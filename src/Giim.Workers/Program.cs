using Giim.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddGiimInfrastructure(
    builder.Configuration.GetConnectionString("Giim")
    ?? throw new InvalidOperationException("Connection string 'Giim' is not configured."));

// Sync jobs (Intune delta, SDP assets, Okta events) are registered here as they are built.

var host = builder.Build();
host.Run();
