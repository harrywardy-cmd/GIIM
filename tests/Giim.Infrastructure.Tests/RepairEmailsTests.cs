using Giim.Domain.Assets;
using Giim.Domain.Repairs;
using Giim.Infrastructure.Notifications;

namespace Giim.Infrastructure.Tests;

public class RepairEmailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(10));
    private static readonly ActionContext Tech = new("jane.tech", "inc4521");
    private static readonly RepairEmailFacts Facts = new("Dell Latitude 7450", "IT-00123", "sam.lee@contoso.example", "Sam Lee",
        "https://giim.example/?asset=1");

    private static Repair Open(string? vendor = null, bool warranty = false)
    {
        var asset = new Asset { SerialNumber = "7JK3L92", AssetTag = "IT-00123", Manufacturer = "Dell", Model = "Latitude 7450" };
        asset.SetStatusFromMigration(AssetStatus.Assigned);
        return Repair.Open(asset, Tech, "Screen flickers", vendor, warranty, vendor is null ? null : "RMA-5521", null);
    }

    [Fact]
    public void Going_for_repair_tells_the_holder_where_and_why()
    {
        var email = RepairEmails.Started(Facts, Open("Dell", warranty: true), Now);

        Assert.Equal(("sam.lee@contoso.example", "Your Dell Latitude 7450 has gone for repair"), (email.ToAddress, email.Subject));
        Assert.Contains("it has gone to Dell under warranty", email.BodyText, StringComparison.Ordinal);
        Assert.Contains("Fault: Screen flickers", email.BodyText, StringComparison.Ordinal);
        Assert.Contains("Vendor reference: RMA-5521", email.BodyText, StringComparison.Ordinal);
        Assert.Contains("Ticket: INC4521", email.BodyText, StringComparison.Ordinal);
        Assert.Contains(Facts.Link!, email.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_repair_says_it_is_with_it()
    {
        Assert.Contains("IT is repairing it", RepairEmails.Started(Facts, Open(), Now).BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public void Repaired_and_beyond_repair_read_differently()
    {
        var fixedOne = Open();
        fixedOne.Complete(Tech, RepairOutcome.Repaired, "Loose cable", "Replaced display cable", 0m);
        var broken = Open();
        broken.Complete(Tech, RepairOutcome.BeyondRepair, "Main board failed", null, null);

        var repaired = RepairEmails.Completed(Facts, fixedOne, Now);
        var notRepaired = RepairEmails.Completed(Facts, broken, Now);

        Assert.Equal("Your Dell Latitude 7450 is repaired", repaired.Subject);
        Assert.Contains("Work done: Replaced display cable", repaired.BodyText, StringComparison.Ordinal);
        Assert.Equal("Your Dell Latitude 7450 can't be repaired", notRepaired.Subject);
        Assert.Contains("IT will arrange a replacement", notRepaired.BodyText, StringComparison.Ordinal);
        Assert.Contains("Diagnosis: Main board failed", notRepaired.BodyText, StringComparison.Ordinal);
    }
}
