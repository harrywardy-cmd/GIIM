using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Tests;

public class ReceiveAssetTests
{
    private static readonly ActionContext Tech = new("john.smith", "INC54321");
    private static readonly Guid Laptop = Guid.NewGuid();

    private static NewAsset Details(string serial = "7jk3l92", string manufacturer = "Dell Inc.") =>
        new(serial, manufacturer, "Latitude 7450", Laptop, AssetTag: " it-00123 ", Location: "IT Store Room",
            PurchaseDate: new DateOnly(2026, 7, 14), WarrantyExpiry: new DateOnly(2029, 7, 14), Supplier: "Dell", Cost: 1849m,
            PurchaseOrder: "PO33445");

    [Fact]
    public void New_device_is_received_with_clean_serial_and_a_first_timeline_entry()
    {
        var (asset, e) = Asset.Receive(Details(), Tech);

        Assert.Equal("7JK3L92", asset.SerialNumber);
        Assert.Equal("IT-00123", asset.AssetTag);
        Assert.Equal("Dell", asset.Manufacturer);          // "Dell Inc." normalised like imports
        Assert.Equal(AssetStatus.Received, asset.Status);
        Assert.Equal(AssetEventType.Created, e.Type);
        Assert.Equal(AssetStatus.Received, e.ToStatus);
        Assert.Equal("john.smith", e.Actor);
        Assert.Equal("INC54321", e.TicketNumber);
        Assert.Contains("PO33445", e.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_spare_can_start_ready_to_deploy()
    {
        var (asset, _) = Asset.Receive(Details(), Tech, AssetStatus.ReadyToDeploy);

        Assert.Equal(AssetStatus.ReadyToDeploy, asset.Status);
    }

    [Fact]
    public void Received_device_follows_the_lifecycle()
    {
        var (asset, _) = Asset.Receive(Details(), Tech);

        Assert.Throws<DomainException>(() => asset.Assign(Tech, Guid.NewGuid(), "Harry Ward", null, []));  // not set up yet
        asset.MarkReady(Tech);
        asset.Assign(Tech, Guid.NewGuid(), "Harry Ward", null, []);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("0")]
    public void Missing_or_placeholder_serial_is_rejected(string serial)
    {
        Assert.Throws<DomainException>(() => Asset.Receive(Details(serial: serial), Tech));
    }

    [Fact]
    public void Warranty_before_purchase_is_rejected()
    {
        var bad = Details() with { WarrantyExpiry = new DateOnly(2020, 1, 1) };

        Assert.Throws<DomainException>(() => Asset.Receive(bad, Tech));
    }

    [Theory]
    [InlineData(AssetStatus.Assigned)]
    [InlineData(AssetStatus.Returned)]
    public void New_asset_cannot_start_mid_lifecycle(AssetStatus status)
    {
        Assert.Throws<DomainException>(() => Asset.Receive(Details(), Tech, status));
    }
}
