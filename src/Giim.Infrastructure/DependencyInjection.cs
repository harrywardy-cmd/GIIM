using Giim.Infrastructure.Activity;
using Giim.Infrastructure.Assets;
using Giim.Infrastructure.Devices;
using Giim.Infrastructure.Importing;
using Giim.Infrastructure.Locations;
using Giim.Infrastructure.People;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Reports;
using Giim.Infrastructure.Requests;
using Giim.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Giim.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGiimInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GiimDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AssetImportService>();
        services.AddScoped<AssetLifecycleService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<RepairService>();
        services.AddScoped<LocationService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<ReportService>();
        services.AddScoped<RequestService>();
        services.AddScoped<PeopleSyncService>();
        services.AddScoped<LegacyOwnerService>();
        services.AddScoped<StockService>();
        services.AddScoped<IntuneSyncService>();
        services.AddScoped<ReconciliationService>();

        return services;
    }
}
