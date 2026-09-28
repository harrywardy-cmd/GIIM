using Giim.Domain.Assets;
using Giim.Domain.Assignments;
using Giim.Domain.Auditing;
using Giim.Domain.Cases;
using Giim.Domain.Devices;
using Giim.Domain.People;
using Giim.Domain.Provisioning;
using Giim.Domain.Software;
using Giim.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Persistence;

public sealed class GiimDbContext(DbContextOptions<GiimDbContext> options) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ManagedDevice> ManagedDevices => Set<ManagedDevice>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<RoleProfile> RoleProfiles => Set<RoleProfile>();
    public DbSet<ProfileItem> ProfileItems => Set<ProfileItem>();
    public DbSet<ServiceCase> Cases => Set<ServiceCase>();
    public DbSet<ChecklistTask> ChecklistTasks => Set<ChecklistTask>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Store enums as readable text so the database makes sense in reports and SQL queries.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Department>(e =>
        {
            e.HasIndex(d => d.Code).IsUnique();
            e.Property(d => d.Name).HasMaxLength(200);
            e.Property(d => d.Code).HasMaxLength(20);
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.HasIndex(p => p.EmployeeId).IsUnique();
            e.HasIndex(p => p.UserPrincipalName);
            e.HasIndex(p => p.OktaUserId);
            e.Property(p => p.EmployeeId).HasMaxLength(50);
            e.Property(p => p.DisplayName).HasMaxLength(200);
            e.HasOne(p => p.Department).WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Asset>(e =>
        {
            // Serial number is the key used to match SDP, Excel and Intune records.
            e.HasIndex(a => a.SerialNumber).IsUnique();
            e.HasIndex(a => a.AssetTag).IsUnique().HasFilter("[AssetTag] IS NOT NULL");
            e.HasIndex(a => a.IntuneDeviceId);
            e.HasIndex(a => a.Status);
            e.Property(a => a.SerialNumber).HasMaxLength(100);
            e.Property(a => a.AssetTag).HasMaxLength(50);
            e.Property(a => a.LegacyAssignedTo).HasMaxLength(200);
            e.HasOne(a => a.Category).WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssetCategory>(e =>
        {
            e.ToTable("AssetCategories");
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasData(AssetCategory.Defaults.Select(c => new
            {
                c.Id, c.CreatedAt, c.Name, c.IsIntuneManaged, c.ReturnOnOffboarding, c.IsActive,
            }));
        });

        modelBuilder.Entity<ManagedDevice>(e =>
        {
            e.HasIndex(d => d.IntuneId).IsUnique();
            e.HasIndex(d => d.SerialNumber);
            e.HasIndex(d => d.UserPrincipalName);
            e.Property(d => d.IntuneId).HasMaxLength(64);
            e.Property(d => d.DeviceName).HasMaxLength(256);
            e.Property(d => d.SerialNumber).HasMaxLength(100);
            e.Property(d => d.Manufacturer).HasMaxLength(100);
            e.Property(d => d.Model).HasMaxLength(200);
            e.Property(d => d.OperatingSystem).HasMaxLength(50);
            e.Property(d => d.UserPrincipalName).HasMaxLength(256);
            e.Property(d => d.ComplianceState).HasMaxLength(50);
        });

        modelBuilder.Entity<SyncRun>(e =>
        {
            e.HasIndex(r => new { r.Source, r.StartedAt });
            e.Property(r => r.Source).HasMaxLength(50);
            e.Property(r => r.Error).HasMaxLength(2000);
        });

        modelBuilder.Entity<StockItem>(e =>
        {
            e.HasIndex(s => s.Name).IsUnique();
            e.Property(s => s.Name).HasMaxLength(150);
            e.Property(s => s.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.HasIndex(m => new { m.StockItemId, m.Location });
            e.HasIndex(m => m.CreatedAt);
            e.Property(m => m.Location).HasMaxLength(150);
            e.Property(m => m.Note).HasMaxLength(500);
            e.Property(m => m.ServiceDeskRequestId).HasMaxLength(50);
            e.Property(m => m.Actor).HasMaxLength(200);
            e.HasOne<StockItem>().WithMany().HasForeignKey(m => m.StockItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Application>(e =>
        {
            e.HasIndex(a => a.Name).IsUnique();
            e.Property(a => a.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasIndex(a => new { a.PersonId, a.EndedAt });
            e.HasIndex(a => a.AssetId);
            e.HasIndex(a => a.ServiceDeskRequestId);
            e.Ignore(a => a.IsActive);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Assignment_OneTarget",
                "([AssetId] IS NOT NULL AND [ApplicationId] IS NULL) OR ([AssetId] IS NULL AND [ApplicationId] IS NOT NULL)"));
        });

        modelBuilder.Entity<RoleProfile>(e =>
            e.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.RoleProfileId));

        modelBuilder.Entity<ServiceCase>(e =>
        {
            e.ToTable("Cases");
            e.HasIndex(c => c.ServiceDeskRequestId);
            e.HasIndex(c => c.Status);
            e.Ignore(c => c.AllTasksFinished);
            e.HasMany(c => c.Tasks).WithOne().HasForeignKey(t => t.CaseId);
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });
    }
}
