using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Giim.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef`. Targets the local Docker SQL Server (docker-compose.yml) unless the
/// ConnectionStrings__Giim environment variable points somewhere else.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GiimDbContext>
{
    private const string LocalDocker =
        "Server=localhost,1433;Database=Giim;User Id=sa;Password=Giim-Dev-Passw0rd!;TrustServerCertificate=True";

    public GiimDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GiimDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__Giim") ?? LocalDocker)
            .Options);
}
