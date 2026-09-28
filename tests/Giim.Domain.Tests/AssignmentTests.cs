using System.Text.Json;
using Giim.Domain.Assets;
using Giim.Domain.Assignments;
using Giim.Domain.Common;

namespace Giim.Domain.Tests;

public class AssignmentTests
{
    private static readonly ActionContext Tech = new("john.smith", "INC54321");
    private static readonly Guid Harry = Guid.NewGuid();

    private static Asset Laptop(AssetStatus status = AssetStatus.ReadyToDeploy)
    {
        var asset = new Asset { SerialNumber = "7JK3L92", AssetTag = "IT-00123", Manufacturer = "Dell", Model = "Latitude 7450" };
        asset.SetStatusFromMigration(status);
        return asset;
    }

    [Fact]
    public void Assign_records_person_location_and_accessories()
    {
        var laptop = Laptop();

        var melbourne = new Locations.Location { Name = "Melbourne Office" };
        var e = laptop.Assign(Tech, Harry, "Harry Ward", melbourne, ["Dell Dock WD22TB4", "Charger"]);

        Assert.Equal(AssetStatus.Assigned, laptop.Status);
        Assert.Equal(Harry, laptop.AssignedToPersonId);
        Assert.Equal(melbourne.Id, laptop.LocationId);
        Assert.Equal("Assigned to Harry Ward", e.Summary);
        var details = JsonDocument.Parse(e.DetailsJson!).RootElement;
        Assert.Equal(2, details.GetProperty("Accessories").GetArrayLength());
    }

    [Theory]
    [InlineData(AssetStatus.Returned)]   // must be wiped first
    [InlineData(AssetStatus.Received)]   // must be set up first
    [InlineData(AssetStatus.InRepair)]   // repairs finish through the repair workflow
    [InlineData(AssetStatus.Assigned)]   // already with someone
    public void Only_ready_assets_can_be_assigned(AssetStatus status)
    {
        Assert.Throws<DomainException>(() => Laptop(status).Assign(Tech, Harry, "Harry Ward", null, []));
    }

    [Fact]
    public void Return_clears_the_holder_and_lists_missing_accessories()
    {
        var laptop = Laptop();
        laptop.Assign(Tech, Harry, "Harry Ward", null, []);

        var e = laptop.Return(Tech, AssetCondition.Good, "Harry Ward", "Harry Ward", ["Charger"]);

        Assert.Equal(AssetStatus.Returned, laptop.Status);
        Assert.Null(laptop.AssignedToPersonId);
        Assert.Equal("Returned (good) from Harry Ward; missing: Charger", e.Summary);
    }

    [Fact]
    public void Return_request_keeps_the_device_with_its_holder()
    {
        var laptop = Laptop();
        laptop.Assign(Tech, Harry, "Harry Ward", null, []);

        laptop.RequestReturn(Tech, new DateOnly(2026, 10, 31));

        Assert.Equal(AssetStatus.ReturnRequested, laptop.Status);
        Assert.Equal(Harry, laptop.AssignedToPersonId);
        laptop.Return(Tech, AssetCondition.Fair, null, "Harry Ward", []);
        Assert.Equal(AssetStatus.Returned, laptop.Status);
    }

    [Fact]
    public void Completing_an_assignment_marks_unticked_accessories_missing()
    {
        var assignment = Assignment.Start(Harry, Guid.NewGuid(), Tech);
        var dock = new AccessoryLine { Description = "Dell Dock WD22TB4", AccessoryAssetId = Guid.NewGuid() };
        var charger = new AccessoryLine { Description = "Charger" };
        var mice = new AccessoryLine { Description = "Mouse", Quantity = 2 };
        assignment.Accessories.AddRange([dock, charger, mice]);

        var missing = assignment.Complete(new ActionContext("jane.tech", "inc55421"), AssetCondition.Good, "Harry Ward",
            new HashSet<Guid> { dock.Id, mice.Id });

        Assert.Equal([charger], missing);
        Assert.Equal(AccessoryStatus.Returned, dock.Status);
        Assert.False(assignment.IsActive);
        Assert.Equal("jane.tech", assignment.ReceivedBy);
        Assert.Equal("INC55421", assignment.ReturnTicketNumber);
        Assert.Equal("Mouse x2", mice.Label);
    }

    [Fact]
    public void Assignment_cannot_end_twice()
    {
        var assignment = Assignment.Start(Harry, Guid.NewGuid(), Tech);
        assignment.Complete(Tech, AssetCondition.Good, null, new HashSet<Guid>());

        Assert.Throws<DomainException>(() => assignment.Complete(Tech, AssetCondition.Good, null, new HashSet<Guid>()));
    }

    [Fact]
    public void Returned_accessories_must_belong_to_the_assignment()
    {
        var assignment = Assignment.Start(Harry, Guid.NewGuid(), Tech);

        Assert.Throws<DomainException>(() =>
            assignment.Complete(Tech, AssetCondition.Good, null, new HashSet<Guid> { Guid.NewGuid() }));
    }

    [Fact]
    public void Start_records_technician_and_ticket()
    {
        var assignment = Assignment.Start(Harry, Guid.NewGuid(), new ActionContext("john.smith", " inc54321 ", "New starter"));

        Assert.Equal("john.smith", assignment.AssignedBy);
        Assert.Equal("INC54321", assignment.ServiceDeskRequestId);
        Assert.Equal("New starter", assignment.Notes);
    }
}
