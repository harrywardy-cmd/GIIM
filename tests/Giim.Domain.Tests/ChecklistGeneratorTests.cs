using Giim.Domain.Assets;
using Giim.Domain.Cases;
using Giim.Domain.People;
using Giim.Domain.Provisioning;
using Giim.Domain.Software;

namespace Giim.Domain.Tests;

public class ChecklistGeneratorTests
{
    private static Person NewPerson(ProvisioningTrack track = ProvisioningTrack.Full) => new()
    {
        EmployeeId = "E1001",
        DisplayName = "Test Starter",
        Track = track,
        StartDate = new DateOnly(2026, 10, 12),
    };

    [Fact]
    public void Onboarding_includes_every_profile_item_linked_to_its_source()
    {
        var profile = new RoleProfile { Name = "Finance - Default" };
        var laptop = new ProfileItem { Type = ProfileItemType.Hardware, Description = "Standard laptop", HardwareCategory = AssetCategory.Laptop };
        var xero = new ProfileItem { Type = ProfileItemType.Application, Description = "Xero", GroupName = "APP-Xero-Users" };
        profile.Items.AddRange([laptop, xero]);

        var serviceCase = ChecklistGenerator.ForOnboarding(NewPerson(), profile, "SDP-10452");

        Assert.Equal(CaseType.Onboarding, serviceCase.Type);
        Assert.Equal("SDP-10452", serviceCase.ServiceDeskRequestId);
        Assert.Contains(serviceCase.Tasks, t => t.SourceId == laptop.Id && t.Kind == TaskKind.Manual);
        Assert.Contains(serviceCase.Tasks, t => t.SourceId == xero.Id && t.Kind == TaskKind.Automated);
        Assert.Equal(Enumerable.Range(1, serviceCase.Tasks.Count), serviceCase.Tasks.Select(t => t.Order));
    }

    [Fact]
    public void Light_track_onboarding_skips_mailbox()
    {
        var serviceCase = ChecklistGenerator.ForOnboarding(NewPerson(ProvisioningTrack.Light), new RoleProfile { Name = "Site" }, null);

        Assert.DoesNotContain(serviceCase.Tasks, t => t.Title.Contains("mailbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Offboarding_is_built_from_actual_assignments()
    {
        var monitor = new Asset { SerialNumber = "CN-0ABC", Manufacturer = "Dell", Model = "P2723", Category = AssetCategory.Monitor };
        var visio = new Application { Name = "Visio" };

        var serviceCase = ChecklistGenerator.ForOffboarding(NewPerson(), [monitor], [visio], "SDP-20001");

        Assert.Contains(serviceCase.Tasks, t => t.SourceId == monitor.Id && t.Title.Contains("CN-0ABC"));
        Assert.Contains(serviceCase.Tasks, t => t.SourceId == visio.Id && t.Kind == TaskKind.Manual);
    }

    [Fact]
    public void Destructive_offboarding_steps_require_approval()
    {
        var serviceCase = ChecklistGenerator.ForOffboarding(NewPerson(), [], [], null);

        var disable = Assert.Single(serviceCase.Tasks, t => t.Title.StartsWith("Disable AD account", StringComparison.Ordinal));
        Assert.True(disable.RequiresApproval);
    }
}
