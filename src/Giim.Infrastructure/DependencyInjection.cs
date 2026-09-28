using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Giim.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGiimInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GiimDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        return services;
    }
}
