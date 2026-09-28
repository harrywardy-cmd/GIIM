using Giim.Domain.Assets;
using Giim.Domain.Common;
using Giim.Domain.Repairs;

namespace Giim.Domain.Tests;

public class RepairAndRetirementTests
{
    private static readonly ActionContext Tech = new("john.smith", "INC56111");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Asset Laptop(AssetStatus status)
    {
        var asset = new Asset { SerialNumber = "7JK3L92", AssetTag = "IT-00123", Manufacturer = "Dell", Model = "Latitude 7450" };
        asset.SetStatusFromMigration(status);
        return asset;
    }

    private static Asset AssignedLaptop()
    {
        var asset = Laptop(AssetStatus.ReadyToDeploy);
        asset.Assign(Tech, Guid.NewGuid(), "Harry Ward", null, []);
        return asset;
    }

    private static Repair Send(Asset asset, string? vendor = "Dell", bool warranty = true)
    {
        var repair = Repair.Open(asset, Tech, "Laptop will not charge", vendor, warranty, "RMA-7781", Today);
        asset.SendToRepair(Tech, repair);
        return repair;
    }

    private static void Fix(Asset asset, Repair repair)
    {
        repair.Complete(Tech, RepairOutcome.Repaired, "Damaged charging port", "Charging port replaced", 0m);
        asset.CompleteRepair(Tech, repair);
    }

    // ---- repairs -------------------------------------------------------------------------------------------

    [Fact]
    public void Repair_records_fault_vendor_and_warranty()
    {
        var asset = AssignedLaptop();

        var repair = Send(asset);

        Assert.Equal(AssetStatus.InRepair, asset.Status);
        Assert.Equal(AssetStatus.Assigned, repair.StartedFromStatus);
        Assert.True(repair.IsOpen);
        Assert.Equal("RMA-7781", repair.VendorReference);
    }

    [Fact]
    public void Repaired_device_goes_back_to_its_user()
    {
        var asset = AssignedLaptop();
        var holder = asset.AssignedToPersonId;

        Fix(asset, Send(asset));

        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Equal(holder, asset.AssignedToPersonId);
    }

    [Fact]
    public void Repair_cannot_skip_the_wipe_of_a_returned_device()
    {
        var asset = Laptop(AssetStatus.Returned);

        Fix(asset, Send(asset, vendor: null, warranty: false));

        Assert.Equal(AssetStatus.Returned, asset.Status);   // still needs wiping before it can be deployed
    }

    [Theory]
    [InlineData(AssetStatus.ReadyToDeploy, AssetStatus.ReadyToDeploy)]
    [InlineData(AssetStatus.Wiped, AssetStatus.ReadyToDeploy)]
    [InlineData(AssetStatus.Received, AssetStatus.Received)]
    public void Repaired_device_returns_to_the_stage_it_came_from(AssetStatus from, AssetStatus expected)
    {
        var asset = Laptop(from);

        Fix(asset, Send(asset));

        Assert.Equal(expected, asset.Status);
    }

    [Fact]
    public void Repaired_outcome_needs_the_work_recorded()
    {
        var asset = Laptop(AssetStatus.ReadyToDeploy);
        var repair = Send(asset);

        Assert.Throws<DomainException>(() => repair.Complete(Tech, RepairOutcome.Repaired, "Board fault", " ", 120m));
    }

    [Fact]
    public void Warranty_claim_needs_a_vendor()
    {
        Assert.Throws<DomainException>(() => Repair.Open(Laptop(AssetStatus.ReadyToDeploy), Tech, "Dead", null, true, null, null));
    }

    [Fact]
    public void Repair_can_only_be_completed_once()
    {
        var asset = Laptop(AssetStatus.ReadyToDeploy);
        var repair = Send(asset);
        Fix(asset, repair);

        Assert.Throws<DomainException>(() => repair.Complete(Tech, RepairOutcome.Repaired, null, "Again", null));
    }

    [Fact]
    public void Beyond_repair_device_stays_in_repair_until_retired()
    {
        var asset = Laptop(AssetStatus.ReadyToDeploy);
        var repair = Send(asset, vendor: null, warranty: false);
        repair.Complete(Tech, RepairOutcome.BeyondRepair, "Motherboard failure", null, null);

        var e = asset.CompleteRepair(Tech, repair);

        Assert.Equal(AssetStatus.InRepair, asset.Status);
        Assert.Equal("Beyond repair", e.Summary);
        asset.Retire(Tech, "Beyond economical repair", DataSanitisation.DriveDestroyed, "IT Store Room");
        Assert.Equal(AssetStatus.Retired, asset.Status);
    }

    // ---- retirement ----------------------------------------------------------------------------------------

    [Fact]
    public void Retirement_records_reason_and_data_sanitisation()
    {
        var asset = Laptop(AssetStatus.Wiped);

        asset.Retire(Tech, "End of life (5 years)", DataSanitisation.Wiped, "E-waste cage");

        Assert.Equal(AssetStatus.Retired, asset.Status);
        Assert.Equal(DataSanitisation.Wiped, asset.DataSanitisation);
        Assert.Equal("E-waste cage", asset.Location);
        Assert.NotNull(asset.RetiredAt);
    }

    [Theory]
    [InlineData(DataSanitisation.NotPossible)]
    [InlineData(DataSanitisation.RemoteWipe)]
    public void Device_in_hand_must_actually_be_wiped(DataSanitisation sanitisation)
    {
        Assert.Throws<DomainException>(() => Laptop(AssetStatus.Returned).Retire(Tech, "Old", sanitisation, null));
    }

    [Theory]
    [InlineData(AssetStatus.Lost, DataSanitisation.NotPossible)]
    [InlineData(AssetStatus.Stolen, DataSanitisation.RemoteWipe)]
    public void Lost_or_stolen_device_is_retired_with_what_was_possible(AssetStatus status, DataSanitisation sanitisation)
    {
        var asset = Laptop(status);

        asset.Retire(Tech, "Written off", sanitisation, null);

        Assert.Equal(AssetStatus.Retired, asset.Status);
    }

    [Fact]
    public void Lost_device_cannot_claim_a_local_wipe()
    {
        Assert.Throws<DomainException>(() => Laptop(AssetStatus.Lost).Retire(Tech, "Written off", DataSanitisation.Wiped, null));
    }

    [Fact]
    public void Device_still_with_a_person_must_be_returned_before_retirement()
    {
        var asset = AssignedLaptop();
        var repair = Send(asset, vendor: null, warranty: false);
        repair.Complete(Tech, RepairOutcome.BeyondRepair, "Water damage", null, null);
        asset.CompleteRepair(Tech, repair);

        var error = Assert.Throws<DomainException>(() => asset.Retire(Tech, "Beyond repair", DataSanitisation.DriveDestroyed, null));
        Assert.Contains("return it first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assigned_device_cannot_be_retired_directly()
    {
        Assert.Throws<DomainException>(() => AssignedLaptop().Retire(Tech, "Old", DataSanitisation.Wiped, null));
    }

    // ---- disposal ------------------------------------------------------------------------------------------

    [Fact]
    public void E_waste_disposal_needs_company_and_certificate()
    {
        var asset = Laptop(AssetStatus.Retired);

        Assert.Throws<DomainException>(() => asset.RecordDisposal(Tech, DisposalMethod.EWasteRecycling, "Greenwaste Pty Ltd", null, Today));
        Assert.Throws<DomainException>(() => asset.RecordDisposal(Tech, DisposalMethod.EWasteRecycling, null, "EW-88321", Today));

        var e = asset.RecordDisposal(Tech, DisposalMethod.EWasteRecycling, "Greenwaste Pty Ltd", "EW-88321", Today);

        Assert.Equal(AssetStatus.Disposed, asset.Status);
        Assert.Equal("EW-88321", asset.DisposalCertificate);
        Assert.Equal("Disposed (e-waste recycling)", e.Summary);
    }

    [Fact]
    public void Sold_device_needs_no_certificate()
    {
        var asset = Laptop(AssetStatus.Retired);

        asset.RecordDisposal(Tech, DisposalMethod.Sold, null, null, Today);

        Assert.Equal(AssetStatus.Disposed, asset.Status);
    }

    [Theory]
    [InlineData(AssetStatus.ReadyToDeploy)]
    [InlineData(AssetStatus.Returned)]
    public void Only_retired_assets_can_be_disposed(AssetStatus status)
    {
        Assert.Throws<DomainException>(() => Laptop(status).RecordDisposal(Tech, DisposalMethod.Sold, null, null, Today));
    }
}
