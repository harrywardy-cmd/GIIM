using Giim.Domain.Assets;
using Giim.Domain.Assignments;
using Giim.Domain.Auditing;
using Giim.Domain.Cases;
using Giim.Domain.Devices;
using Giim.Domain.Locations;
using Giim.Domain.Notifications;
using Giim.Domain.People;
using Giim.Domain.Repairs;
using Giim.Domain.Provisioning;
using Giim.Domain.Requests;
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
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();
    public DbSet<Repair> Repairs => Set<Repair>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>History tables are append-only: a change or delete is a bug, so fail loudly.</summary>
    private void GuardAppendOnly()
    {
        var tampered = ChangeTracker.Entries()
            .Where(e => e.Entity is AssetEvent or AuditEntry or StockMovement or DeviceRequestEvent)
            .FirstOrDefault(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (tampered is not null)
            throw new InvalidOperationException($"{tampered.Entity.GetType().Name} records are append-only and cannot be {tampered.State.ToString().ToLowerInvariant()}.");
    }
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
    public DbSet<DeviceRequest> DeviceRequests => Set<DeviceRequest>();
    public DbSet<DeviceRequestEvent> DeviceRequestEvents => Set<DeviceRequestEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Numbers device requests REQ1001, REQ1002...</summary>
    public const string RequestNumberSequence = "DeviceRequestNumbers";

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
            e.Property(p => p.EmployeeId).HasMaxLength(50);
            e.Property(p => p.DisplayName).HasMaxLength(200);
            e.HasOne(p => p.Department).WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.HasIndex(l => l.Name).IsUnique();
            e.Property(l => l.Name).HasMaxLength(Location.MaxNameLength);
            e.Property(l => l.Address).HasMaxLength(300);
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
            e.Property(a => a.LegacyDepartment).HasMaxLength(100);
            e.Property(a => a.RetirementReason).HasMaxLength(500);
            e.Property(a => a.DisposalCompany).HasMaxLength(200);
            e.Property(a => a.DisposalCertificate).HasMaxLength(100);
            e.HasIndex(a => a.DisposalCertificate);
            e.HasOne(a => a.Category).WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(a => a.DisplayName);
            e.HasIndex(a => a.AssignedToPersonId);
            e.HasOne<Person>().WithMany().HasForeignKey(a => a.AssignedToPersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.LocationId);
            e.HasOne<Location>().WithMany().HasForeignKey(a => a.LocationId).OnDelete(DeleteBehavior.Restrict);
            // Optimistic concurrency: if two technicians act on the same asset at once, the second save fails
            // instead of silently overwriting the first.
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<Repair>(e =>
        {
            e.HasIndex(r => new { r.AssetId, r.OpenedAt });
            // An asset can only be in one repair at a time.
            e.HasIndex(r => r.AssetId, "UX_Repairs_OneOpenPerAsset").IsUnique().HasFilter("[CompletedAt] IS NULL");
            e.HasIndex(r => r.VendorReference);
            e.HasIndex(r => r.TicketNumber);
            e.Property(r => r.Fault).HasMaxLength(1000);
            e.Property(r => r.Vendor).HasMaxLength(200);
            e.Property(r => r.VendorReference).HasMaxLength(100);
            e.Property(r => r.OpenedBy).HasMaxLength(200);
            e.Property(r => r.CompletedBy).HasMaxLength(200);
            e.Property(r => r.TicketNumber).HasMaxLength(50);
            e.Property(r => r.Diagnosis).HasMaxLength(2000);
            e.Property(r => r.WorkPerformed).HasMaxLength(2000);
            e.Ignore(r => r.IsOpen);
            e.Ignore(r => r.Duration);
            e.HasOne<Asset>().WithMany().HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssetEvent>(e =>
        {
            e.ToTable("AssetEvents");
            e.HasIndex(x => new { x.AssetId, x.OccurredAt });
            e.HasIndex(x => x.TicketNumber);
            e.HasIndex(x => x.Actor);
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.TicketNumber).HasMaxLength(50);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasIndex(m => new { m.StockItemId, m.LocationId });
            e.HasIndex(m => m.CreatedAt);
            e.Property(m => m.Location).HasMaxLength(Location.MaxNameLength);
            e.HasOne<Location>().WithMany().HasForeignKey(m => m.LocationId).OnDelete(DeleteBehavior.Restrict);
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
            // Safety net under the domain rules: an asset can only be with one person at a time.
            e.HasIndex(a => a.AssetId, "UX_Assignments_OneActivePerAsset").IsUnique()
                .HasFilter("[EndedAt] IS NULL AND [AssetId] IS NOT NULL");
            e.Property(a => a.AssignedBy).HasMaxLength(200);
            e.Property(a => a.Notes).HasMaxLength(1000);
            e.Property(a => a.ReceivedBy).HasMaxLength(200);
            e.Property(a => a.ReturnedBy).HasMaxLength(200);
            e.Property(a => a.ReturnTicketNumber).HasMaxLength(50);
            e.OwnsMany(a => a.Accessories, acc =>
            {
                acc.ToTable("AssignmentAccessories");
                acc.WithOwner().HasForeignKey("AssignmentId");
                acc.HasKey(x => x.Id);
                acc.Property(x => x.Id).ValueGeneratedNever();
                acc.Property(x => x.Description).HasMaxLength(300);
                acc.Ignore(x => x.Label);
                acc.HasIndex(x => x.AccessoryAssetId);
            });
            e.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(a => a.AssetId).OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.HasSequence<int>(RequestNumberSequence).StartsAt(1001);

        modelBuilder.Entity<DeviceRequest>(e =>
        {
            e.HasIndex(r => r.Number).IsUnique();
            e.HasIndex(r => new { r.Status, r.SubmittedAt });
            e.HasIndex(r => r.ApproverPersonId);
            e.HasIndex(r => r.RecipientPersonId);
            e.HasIndex(r => r.RequestedBy);
            e.HasIndex(r => r.TicketNumber);
            e.HasIndex(r => r.PurchaseOrder);
            e.HasIndex(r => r.AssetId);
            e.Ignore(r => r.Reference);
            e.Ignore(r => r.IsClosed);
            foreach (var login in new[] { nameof(DeviceRequest.RequestedBy), nameof(DeviceRequest.DecidedBy) })
                e.Property(login).HasMaxLength(200);
            foreach (var name in new[] { nameof(DeviceRequest.RequestedByName), nameof(DeviceRequest.DecidedByName), nameof(DeviceRequest.Supplier) })
                e.Property(name).HasMaxLength(200);
            e.Property(r => r.RequestedByEmail).HasMaxLength(256);
            e.Property(r => r.DeviceDescription).HasMaxLength(200);
            e.Property(r => r.Specifications).HasMaxLength(1000);
            e.Property(r => r.Reason).HasMaxLength(1000);
            e.Property(r => r.Notes).HasMaxLength(2000);
            e.Property(r => r.TicketNumber).HasMaxLength(50);
            e.Property(r => r.BudgetCode).HasMaxLength(50);
            e.Property(r => r.DecisionComment).HasMaxLength(1000);
            e.Property(r => r.PurchaseOrder).HasMaxLength(50);
            e.Property(r => r.TrackingNumber).HasMaxLength(100);
            e.Property(r => r.PurchaseNotes).HasMaxLength(1000);
            e.Property(r => r.CancellationReason).HasMaxLength(1000);
            e.HasOne<Person>().WithMany().HasForeignKey(r => r.RecipientPersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Person>().WithMany().HasForeignKey(r => r.ApproverPersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssetCategory>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Restrict);
            // Two people acting on the same request at once: the second save fails instead of overwriting the first.
            e.Property<byte[]>("RowVersion").IsRowVersion();
        });

        modelBuilder.Entity<DeviceRequestEvent>(e =>
        {
            e.ToTable("DeviceRequestEvents");
            e.HasIndex(x => new { x.RequestId, x.OccurredAt });
            e.HasIndex(x => x.Actor);
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne<DeviceRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.HasIndex(n => new { n.Status, n.NextAttemptAt });
            e.HasIndex(n => n.RequestId);
            e.Property(n => n.Kind).HasMaxLength(50);
            e.Property(n => n.ToAddress).HasMaxLength(256);
            e.Property(n => n.ToName).HasMaxLength(200);
            e.Property(n => n.Subject).HasMaxLength(300);
            e.Property(n => n.LastError).HasMaxLength(1000);
        });
    }
}
