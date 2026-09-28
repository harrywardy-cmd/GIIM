using Giim.Domain.Assets;
using Giim.Domain.Auditing;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Tests;

/// <summary>
/// The guard runs before anything is sent to SQL Server, so these tests need no database:
/// the connection string is never opened.
/// </summary>
public class AppendOnlyGuardTests
{
    private static GiimDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GiimDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

    private static AssetEvent Event() => new() { Id = 1, AssetId = Guid.NewGuid(), Actor = "tech", Summary = "Created" };

    [Fact]
    public void Editing_a_timeline_event_is_refused()
    {
        using var db = NewContext();
        db.AssetEvents.Attach(Event());
        db.Entry(db.AssetEvents.Local.Single()).Property(e => e.Summary).CurrentValue = "rewritten";

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_timeline_event_is_refused()
    {
        await using var db = NewContext();
        db.AssetEvents.Remove(db.AssetEvents.Attach(Event()).Entity);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Deleting_an_audit_entry_is_refused()
    {
        using var db = NewContext();
        db.AuditEntries.Remove(db.AuditEntries.Attach(new AuditEntry { Id = 1, Actor = "a", Action = "b", EntityType = "c" }).Entity);

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
