using Giim.Domain.Assets;
using Giim.Domain.Reconciliation;

namespace Giim.Domain.Tests;

public class ReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);

    private static RegisterEntry Asset(string serial, AssetStatus status = AssetStatus.Assigned, string? owner = "Grace Brown",
        bool intuneManaged = true, string category = "Laptop") =>
        new(Guid.NewGuid(), serial, null, category, intuneManaged, status, owner);

    private static IntuneEntry Device(string? serial, int daysSinceSync = 1, string? upn = "grace.brown@giim-test.local") =>
        new(Guid.NewGuid().ToString(), serial, $"PC-{serial}", "EliteBook", upn, Now.AddDays(-daysSinceSync));

    private static ReconciliationRow Single(IEnumerable<RegisterEntry> register, IEnumerable<IntuneEntry> intune) =>
        Assert.Single(Reconciler.Reconcile(register, intune, Now));

    [Fact]
    public void Matching_device_in_use_by_its_owner_is_clean()
    {
        var row = Single([Asset("S1")], [Device("S1")]);

        Assert.True(row.IsClean);
        Assert.NotNull(row.Register);
        Assert.NotNull(row.Intune);
    }

    [Fact]
    public void Device_only_in_intune_is_reported()
    {
        var row = Single([], [Device("S1")]);

        Assert.Equal([Finding.IntuneOnly], row.Findings);
    }

    [Fact]
    public void Assigned_laptop_missing_from_intune_is_reported()
    {
        Assert.Equal([Finding.NotInIntune], Single([Asset("S1")], []).Findings);
    }

    [Fact]
    public void Monitors_and_spares_are_not_expected_in_intune()
    {
        var rows = Reconciler.Reconcile(
            [Asset("MON1", intuneManaged: false, category: "Monitor"), Asset("SPARE1", AssetStatus.ReadyToDeploy, owner: null)],
            [], Now);

        Assert.All(rows, r => Assert.True(r.IsClean));
    }

    [Theory]
    [InlineData(90, false)]
    [InlineData(91, true)]
    public void Device_not_seen_for_over_90_days_is_stale(int days, bool stale)
    {
        var row = Single([Asset("S1")], [Device("S1", daysSinceSync: days)]);

        Assert.Equal(stale, row.Findings.Contains(Finding.Stale));
    }

    [Theory]
    [InlineData(AssetStatus.Returned)]
    [InlineData(AssetStatus.ReadyToDeploy)]
    [InlineData(AssetStatus.Lost)]
    [InlineData(AssetStatus.Disposed)]
    public void Recently_used_device_the_register_thinks_is_back_is_a_conflict(AssetStatus status)
    {
        var row = Single([Asset("S1", status)], [Device("S1", daysSinceSync: 3)]);

        Assert.Contains(Finding.StatusConflict, row.Findings);
    }

    [Fact]
    public void Returned_device_that_has_gone_quiet_is_not_a_conflict()
    {
        var row = Single([Asset("S1", AssetStatus.Returned)], [Device("S1", daysSinceSync: 45)]);

        Assert.DoesNotContain(Finding.StatusConflict, row.Findings);
    }

    [Fact]
    public void Different_user_in_intune_is_an_owner_mismatch()
    {
        var row = Single([Asset("S1", owner: "Grace Brown")], [Device("S1", upn: "jack.smith@giim-test.local")]);

        Assert.Equal([Finding.OwnerMismatch], row.Findings);
    }

    [Theory]
    [InlineData("Grace Brown", "grace.brown@contoso.com", true)]
    [InlineData("Grace Brown", "grace.brown2@contoso.com", true)]
    [InlineData("O'Brien, Liam", "liam.obrien@contoso.com", false)] // surname-first isn't matched; flagged for a human to check
    [InlineData("liam.obrien@contoso.com", "LIAM.OBRIEN@contoso.com", true)]
    [InlineData("Grace Brown", "grace.browning@contoso.com", false)]
    public void Owner_is_compared_with_the_name_in_the_upn(string owner, string upn, bool matches)
    {
        Assert.Equal(matches, Reconciler.OwnerMatches(owner, upn));
    }

    [Fact]
    public void Re_enrolled_device_uses_the_latest_intune_record()
    {
        var old = Device("S1", daysSinceSync: 200);
        var current = Device("S1", daysSinceSync: 2);

        var row = Single([Asset("S1")], [old, current]);

        Assert.Same(current, row.Intune);
        Assert.True(row.IsClean);
    }

    [Fact]
    public void Re_enrolled_device_not_in_register_is_reported_once()
    {
        var rows = Reconciler.Reconcile([], [Device("S1", 200), Device("S1", 2)], Now);

        Assert.Single(rows);
    }

    [Fact]
    public void Device_without_a_serial_is_reported_as_intune_only()
    {
        var row = Single([], [Device(null)]);

        Assert.Null(row.SerialNumber);
        Assert.Equal([Finding.IntuneOnly], row.Findings);
    }
}
