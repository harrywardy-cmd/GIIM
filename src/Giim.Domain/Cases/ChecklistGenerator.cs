using Giim.Domain.Assets;
using Giim.Domain.People;
using Giim.Domain.Provisioning;
using Giim.Domain.Software;

namespace Giim.Domain.Cases;

/// <summary>
/// Builds checklists. Onboarding comes from the department's role profile;
/// offboarding comes from what the person actually holds.
/// </summary>
public static class ChecklistGenerator
{
    public static ServiceCase ForOnboarding(Person person, RoleProfile profile, string? serviceDeskRequestId)
    {
        var serviceCase = NewCase(CaseType.Onboarding, person, serviceDeskRequestId, person.StartDate);

        Add(serviceCase, "Create AD account", TaskKind.Automated);
        Add(serviceCase, "Wait for Okta import and Entra Connect sync", TaskKind.Automated);
        if (person.Track == ProvisioningTrack.Full)
            Add(serviceCase, "Enable remote mailbox (hybrid Exchange)", TaskKind.Automated);

        foreach (var item in profile.Items)
        {
            var (title, kind) = item.Type switch
            {
                ProfileItemType.Application  => ($"Grant app: {item.Description}", item.GroupName is null ? TaskKind.Manual : TaskKind.Automated),
                ProfileItemType.OktaGroup    => ($"Add to Okta group: {item.GroupName}", TaskKind.Automated),
                ProfileItemType.LicenceGroup => ($"Add to licence group: {item.GroupName}", TaskKind.Automated),
                ProfileItemType.Hardware     => ($"Allocate and scan: {item.Description}", TaskKind.Manual),
                ProfileItemType.StockItem    => ($"Issue from stock: {item.Description}", TaskKind.Manual),
                _                            => (item.Description, TaskKind.Manual),
            };
            Add(serviceCase, title, kind, sourceId: item.Id);
        }

        Add(serviceCase, "Activate Okta account on start date", TaskKind.Automated);
        Add(serviceCase, "Send welcome email to manager", TaskKind.Automated);
        return serviceCase;
    }

    public static ServiceCase ForOffboarding(
        Person person,
        IEnumerable<Asset> assignedAssets,
        IEnumerable<Application> assignedApplications,
        string? serviceDeskRequestId)
    {
        var serviceCase = NewCase(CaseType.Offboarding, person, serviceDeskRequestId, person.EndDate);

        Add(serviceCase, "Revoke Okta and Entra sessions", TaskKind.Automated, requiresApproval: true);
        if (person.Track == ProvisioningTrack.Full)
        {
            Add(serviceCase, "Convert mailbox to shared and grant manager access", TaskKind.Automated, requiresApproval: true);
            Add(serviceCase, "Set out-of-office reply", TaskKind.Automated);
        }

        foreach (var app in assignedApplications)
        {
            var kind = app.OktaGroupName is null ? TaskKind.Manual : TaskKind.Automated;
            Add(serviceCase, $"Remove app access: {app.Name}", kind, sourceId: app.Id);
        }

        // Assets whose category isn't returned (Category loaded) are left off; unknown categories are recovered to be safe.
        foreach (var asset in assignedAssets.Where(a => a.Category?.ReturnOnOffboarding ?? true))
            Add(serviceCase, $"Recover {asset.Category?.Name ?? "asset"}: {asset.Manufacturer} {asset.Model} (S/N {asset.SerialNumber})",
                TaskKind.Manual, sourceId: asset.Id);

        Add(serviceCase, "Remove M365 licence", TaskKind.Automated);
        Add(serviceCase, "Disable AD account and move to Leavers OU", TaskKind.Automated, requiresApproval: true);
        return serviceCase;
    }

    private static ServiceCase NewCase(CaseType type, Person person, string? requestId, DateOnly? due) =>
        new() { Type = type, PersonId = person.Id, ServiceDeskRequestId = requestId, DueDate = due };

    private static void Add(ServiceCase serviceCase, string title, TaskKind kind, Guid? sourceId = null, bool requiresApproval = false) =>
        serviceCase.Tasks.Add(new ChecklistTask
        {
            CaseId = serviceCase.Id,
            Order = serviceCase.Tasks.Count + 1,
            Title = title,
            Kind = kind,
            SourceId = sourceId,
            RequiresApproval = requiresApproval,
        });
}
