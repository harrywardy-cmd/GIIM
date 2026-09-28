using System.Text.Json;
using Giim.Domain.Assets;
using Giim.Domain.Common;

namespace Giim.Domain.Tests;

public class AssetLifecycleTests
{
    private static readonly ActionContext Tech = new("john.smith", "inc54321", "Checked by John");

    private static Asset NewLaptop(AssetStatus status = AssetStatus.ReadyToDeploy)
    {
        var asset = new Asset { SerialNumber = "7JK3L92", AssetTag = "IT-00123", Manufacturer = "Dell", Model = "Latitude 7450" };
        asset.SetStatusFromMigration(status);
        return asset;
    }

    [Fact]
    public void New_asset_is_ready_to_deploy()
    {
        Assert.Equal(AssetStatus.ReadyToDeploy, new Asset { SerialNumber = "S", Manufacturer = "M", Model = "X" }.Status);
    }

    [Theory]
    [InlineData(AssetStatus.Returned, AssetStatus.ReadyToDeploy, false)]  // must be wiped first
    [InlineData(AssetStatus.Returned, AssetStatus.Wiped, true)]
    [InlineData(AssetStatus.Wiped, AssetStatus.ReadyToDeploy, true)]
    [InlineData(AssetStatus.Received, AssetStatus.Assigned, false)]       // must be set up first
    [InlineData(AssetStatus.Lost, AssetStatus.ReadyToDeploy, false)]      // found devices come back via Returned
    [InlineData(AssetStatus.Stolen, AssetStatus.Returned, true)]
    [InlineData(AssetStatus.Retired, AssetStatus.Assigned, false)]        // retired kit is not reissued
    [InlineData(AssetStatus.Retired, AssetStatus.Disposed, true)]
    [InlineData(AssetStatus.InRepair, AssetStatus.Assigned, true)]        // repaired and given back to its user
    public void Transitions_follow_the_lifecycle(AssetStatus from, AssetStatus to, bool allowed)
    {
        Assert.Equal(allowed, AssetLifecycle.CanTransition(from, to));
    }

    [Fact]
    public void Disposed_is_final()
    {
        Assert.Empty(AssetLifecycle.NextStatuses(AssetStatus.Disposed));
    }

    [Fact]
    public void Every_status_has_a_rule()
    {
        // Guards against adding a status and forgetting its transitions.
        foreach (var status in Enum.GetValues<AssetStatus>().Where(s => s != AssetStatus.Disposed))
            Assert.NotEmpty(AssetLifecycle.NextStatuses(status));
    }

    [Fact]
    public void Action_returns_a_complete_timeline_event()
    {
        var asset = NewLaptop(AssetStatus.Returned);

        var e = asset.MarkWiped(Tech, "Intune wipe");

        Assert.Equal(AssetStatus.Wiped, asset.Status);
        Assert.Equal(asset.Id, e.AssetId);
        Assert.Equal(AssetEventType.Wiped, e.Type);
        Assert.Equal(AssetStatus.Returned, e.FromStatus);
        Assert.Equal(AssetStatus.Wiped, e.ToStatus);
        Assert.Equal("john.smith", e.Actor);
        Assert.Equal("INC54321", e.TicketNumber);       // tickets are stored upper-case for searching
        Assert.Equal("Checked by John", e.Note);
        Assert.Equal("Intune wipe", JsonDocument.Parse(e.DetailsJson!).RootElement.GetProperty("Method").GetString());
    }

    [Fact]
    public void Returned_device_cannot_be_marked_ready_without_a_wipe()
    {
        var asset = NewLaptop(AssetStatus.Returned);

        var error = Assert.Throws<DomainException>(() => asset.MarkReady(Tech));

        Assert.Contains("Returned to ReadyToDeploy", error.Message, StringComparison.Ordinal);
        Assert.Equal(AssetStatus.Returned, asset.Status);
    }

    [Fact]
    public void Theft_records_the_circumstances_and_police_reference()
    {
        var asset = NewLaptop(AssetStatus.Assigned);

        var e = asset.ReportStolen(Tech, "Taken from car in Richmond", "Harry Ward", "P-2026-1188");

        Assert.Equal(AssetStatus.Stolen, asset.Status);
        var details = JsonDocument.Parse(e.DetailsJson!).RootElement;
        Assert.Equal("Taken from car in Richmond", details.GetProperty("Circumstances").GetString());
        Assert.Equal("P-2026-1188", details.GetProperty("PoliceReference").GetString());
    }

    [Fact]
    public void Loss_requires_circumstances()
    {
        Assert.Throws<DomainException>(() => NewLaptop(AssetStatus.Assigned).ReportLost(Tech, " ", null));
    }

    [Fact]
    public void Recovered_device_comes_back_as_returned_so_it_is_wiped()
    {
        var asset = NewLaptop(AssetStatus.Lost);

        asset.Recover(Tech, "Found in the Level 3 meeting room");

        Assert.Equal(AssetStatus.Returned, asset.Status);
    }

    [Fact]
    public void Only_lost_or_stolen_devices_can_be_recovered()
    {
        Assert.Throws<DomainException>(() => NewLaptop().Recover(Tech, "Desk"));
    }

    [Fact]
    public void Every_action_must_record_who_did_it()
    {
        Assert.Throws<DomainException>(() => NewLaptop(AssetStatus.Wiped).MarkReady(new ActionContext(" ")));
    }

    [Fact]
    public void Actions_cannot_be_dated_in_the_future()
    {
        var future = new ActionContext("john.smith", OccurredAt: DateTimeOffset.UtcNow.AddDays(1));

        Assert.Throws<DomainException>(() => NewLaptop(AssetStatus.Wiped).MarkReady(future));
    }

    [Fact]
    public void Notes_do_not_change_status()
    {
        var asset = NewLaptop(AssetStatus.Assigned);

        var e = asset.AddNote(Tech);

        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Null(e.ToStatus);
        Assert.Equal(AssetEventType.NoteAdded, e.Type);
    }

    [Fact]
    public void Legacy_owner_link_records_who_has_it_without_changing_status()
    {
        var asset = NewLaptop(AssetStatus.Assigned);
        var person = Guid.NewGuid();

        var e = asset.LinkLegacyOwner(Tech, person, "Harry Ward", "name");

        Assert.Equal(person, asset.AssignedToPersonId);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Equal(AssetEventType.OwnerLinked, e.Type);
        Assert.Null(e.ToStatus);
    }

    [Fact]
    public void Legacy_owner_can_only_be_linked_once_and_only_to_assigned_assets()
    {
        var assigned = NewLaptop(AssetStatus.Assigned);
        assigned.LinkLegacyOwner(Tech, Guid.NewGuid(), "Harry Ward", "name");

        Assert.Throws<DomainException>(() => assigned.LinkLegacyOwner(Tech, Guid.NewGuid(), "Someone Else", "name"));
        Assert.Throws<DomainException>(() => NewLaptop(AssetStatus.ReadyToDeploy).LinkLegacyOwner(Tech, Guid.NewGuid(), "Harry Ward", "name"));
    }

    [Fact]
    public void Migration_status_is_only_allowed_before_tracking_starts()
    {
        var asset = NewLaptop(AssetStatus.Wiped);
        asset.MarkReady(Tech);

        Assert.Throws<DomainException>(() => asset.SetStatusFromMigration(AssetStatus.Assigned));
    }
}
