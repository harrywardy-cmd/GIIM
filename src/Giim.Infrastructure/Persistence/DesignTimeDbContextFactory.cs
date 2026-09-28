using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Giim.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to generate migrations; no database connection is opened.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GiimDbContext>
{
    public GiimDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GiimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Giim;Trusted_Connection=True")
            .Options);
}
