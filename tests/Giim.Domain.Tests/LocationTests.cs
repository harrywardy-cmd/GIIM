using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Locations;

namespace Giim.Domain.Tests;

public class LocationTests
{
    private static readonly ActionContext Tech = new("john.smith", "INC60001");

    private static Asset Laptop(AssetStatus status = AssetStatus.ReadyToDeploy)
    {
        var asset = new Asset { SerialNumber = "7JK3L92", AssetTag = "IT-00123", Manufacturer = "Dell", Model = "Latitude 7450" };
        asset.SetStatusFromMigration(status);
        return asset;
    }

    [Theory]
    [InlineData("  IT   Store Room ", "IT Store Room")]
    [InlineData("Melbourne Office", "Melbourne Office")]
    public void Names_are_tidied(string raw, string expected)
    {
        Assert.Equal(expected, Location.CleanName(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_name_is_rejected(string? raw)
    {
        Assert.Throws<DomainException>(() => Location.CleanName(raw));
    }

    [Theory]
    [InlineData("IT Store Room", LocationKind.ItStoreRoom)]
    [InlineData("Distribution Centre", LocationKind.DistributionCentre)]
    [InlineData("Support Office", LocationKind.Office)]
    [InlineData("Working from home", LocationKind.Remote)]
    [InlineData("Level 3 comms room", LocationKind.Other)]
    public void Kind_is_guessed_for_imported_names(string name, LocationKind kind)
    {
        Assert.Equal(kind, Location.GuessKind(name));
    }

    [Fact]
    public void Moving_records_from_and_to_without_changing_status()
    {
        var melbourne = new Location { Name = "Melbourne Office" };
        var sydney = new Location { Name = "Sydney Office" };
        var asset = Laptop(AssetStatus.Assigned);
        asset.MoveTo(Tech, melbourne, null);

        var e = asset.MoveTo(Tech, sydney, melbourne.Name);

        Assert.Equal(sydney.Id, asset.LocationId);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Equal("Moved from Melbourne Office to Sydney Office", e.Summary);
    }

    [Fact]
    public void Moving_to_the_same_place_is_refused()
    {
        var office = new Location { Name = "Melbourne Office" };
        var asset = Laptop();
        asset.MoveTo(Tech, office, null);

        Assert.Throws<DomainException>(() => asset.MoveTo(Tech, office, office.Name));
    }

    [Fact]
    public void Closed_location_cannot_receive_assets()
    {
        var closed = new Location { Name = "Old Sydney Office", IsActive = false };

        Assert.Throws<DomainException>(() => Laptop().MoveTo(Tech, closed, null));
        Assert.Throws<DomainException>(() => Laptop().Assign(Tech, Guid.NewGuid(), "Harry Ward", closed, []));
    }

    [Fact]
    public void Disposed_asset_cannot_move()
    {
        Assert.Throws<DomainException>(() => Laptop(AssetStatus.Disposed).MoveTo(Tech, new Location { Name = "Anywhere" }, null));
    }
}
