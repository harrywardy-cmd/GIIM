using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Tests;

public class AssetLifecycleTests
{
    private static Asset NewLaptop() => new()
    {
        SerialNumber = "5CG1234XYZ",
        Manufacturer = "HP",
        Model = "EliteBook 840 G10",
        Category = AssetCategory.Laptop,
    };

    [Fact]
    public void New_asset_starts_in_stock()
    {
        Assert.Equal(AssetStatus.InStock, NewLaptop().Status);
    }

    [Fact]
    public void Full_return_cycle_is_allowed()
    {
        var asset = NewLaptop();

        asset.ChangeStatus(AssetStatus.Assigned);
        asset.ChangeStatus(AssetStatus.ReturnRequested);
        asset.ChangeStatus(AssetStatus.Returned);
        asset.ChangeStatus(AssetStatus.Wiped);
        asset.ChangeStatus(AssetStatus.InStock);

        Assert.Equal(AssetStatus.InStock, asset.Status);
    }

    [Fact]
    public void Returned_device_cannot_skip_wipe()
    {
        var asset = NewLaptop();
        asset.ChangeStatus(AssetStatus.Assigned);
        asset.ChangeStatus(AssetStatus.ReturnRequested);
        asset.ChangeStatus(AssetStatus.Returned);

        Assert.Throws<DomainException>(() => asset.ChangeStatus(AssetStatus.InStock));
    }

    [Fact]
    public void Disposed_is_final()
    {
        var asset = NewLaptop();
        asset.ChangeStatus(AssetStatus.Disposed);

        foreach (var status in Enum.GetValues<AssetStatus>())
            Assert.False(AssetLifecycle.CanTransition(AssetStatus.Disposed, status));
    }
}
