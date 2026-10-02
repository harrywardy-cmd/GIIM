using Giim.Domain.Assets;
using Giim.Domain.Automation;
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
    public static ServiceCase ForOnboarding(Person person, RoleProfile profile, string? serviceDeskRequestId, string? createdBy = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(profile);
        var serviceCase = NewCase(CaseType.Onboarding, person, serviceDeskRequestId, person.StartDate, createdBy);
        serviceCase.RoleProfileId = profile.Id;

        Add(serviceCase, "Create AD account", TaskKind.Automated, step: AutomationStep.CreateAccount);
        Add(serviceCase, "Wait for Entra Connect to sync the new account to Microsoft 365", TaskKind.Automated,
            step: AutomationStep.WaitForCloudSync);
        if (person.Track == ProvisioningTrack.Full)
            Add(serviceCase, "Enable remote mailbox (hybrid Exchange)", TaskKind.Automated, step: AutomationStep.EnableRemoteMailbox);

        foreach (var item in profile.Items)
        {
            var (title, kind) = item.Type switch
            {
                ProfileItemType.Application   => ($"Grant app: {item.Description}", item.GroupName is null ? TaskKind.Manual : TaskKind.Automated),
                ProfileItemType.SecurityGroup => ($"Add to group: {item.GroupName}", TaskKind.Automated),
                ProfileItemType.LicenceGroup  => ($"Add to licence group: {item.GroupName}", TaskKind.Automated),
                ProfileItemType.Hardware      => ($"Allocate and scan: {item.Description}", TaskKind.Manual),
                ProfileItemType.StockItem     => ($"Issue from stock: {item.Description}", TaskKind.Manual),
                _                             => (item.Description, TaskKind.Manual),
            };
            // Apps with an access group, security groups and licence groups are AD group memberships the agent can add.
            var group = kind == TaskKind.Automated && !string.IsNullOrWhiteSpace(item.GroupName) ? item.GroupName.Trim() : null;
            Add(serviceCase, title, kind, TaskSource.ProfileItem, item.Id,
                categoryId: item.Type == ProfileItemType.Hardware ? item.CategoryId : null,
                step: group is null ? null : AutomationStep.AddToGroup, stepTarget: group);
        }

        Add(serviceCase, "Enable the account on the start date", TaskKind.Automated, step: AutomationStep.EnableAccount);
        Add(serviceCase, "Send welcome email to manager", TaskKind.Automated, step: AutomationStep.SendWelcomeEmail);
        return serviceCase;
    }

    public static ServiceCase ForOffboarding(
        Person person,
        IEnumerable<Asset> assignedAssets,
        IEnumerable<Application> assignedApplications,
        string? serviceDeskRequestId,
        DateOnly? lastDay = null,
        string? createdBy = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        var serviceCase = NewCase(CaseType.Offboarding, person, serviceDeskRequestId, lastDay ?? person.EndDate, createdBy);

        Add(serviceCase, "Revoke Microsoft 365 sign-in sessions", TaskKind.Automated, requiresApproval: true);
        if (person.Track == ProvisioningTrack.Full)
        {
            Add(serviceCase, "Convert mailbox to shared and grant manager access", TaskKind.Automated, requiresApproval: true);
            Add(serviceCase, "Set out-of-office reply", TaskKind.Automated);
        }

        foreach (var app in assignedApplications)
        {
            var kind = app.AccessGroupName is null ? TaskKind.Manual : TaskKind.Automated;
            Add(serviceCase, $"Remove app access: {app.Name}", kind, TaskSource.Application, app.Id);
        }

        // Assets whose category isn't returned (Category loaded) are left off; unknown categories are recovered to be safe.
        foreach (var asset in assignedAssets.Where(a => a.Category?.ReturnOnOffboarding ?? true))
            Add(serviceCase, $"Recover {asset.Category?.Name ?? "asset"}: {asset.Manufacturer} {asset.Model} (S/N {asset.SerialNumber})",
                TaskKind.Manual, TaskSource.Asset, asset.Id);

        Add(serviceCase, "Remove M365 licence", TaskKind.Automated);
        Add(serviceCase, "Disable AD account and move to Leavers OU", TaskKind.Automated, requiresApproval: true);
        return serviceCase;
    }

    private static ServiceCase NewCase(CaseType type, Person person, string? requestId, DateOnly? due, string? createdBy) =>
        new()
        {
            Type = type,
            PersonId = person.Id,
            ServiceDeskRequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim().ToUpperInvariant(),
            DueDate = due,
            CreatedBy = createdBy,
        };

    private static void Add(ServiceCase serviceCase, string title, TaskKind kind, TaskSource source = TaskSource.None,
        Guid? sourceId = null, Guid? categoryId = null, bool requiresApproval = false, AutomationStep? step = null,
        string? stepTarget = null) =>
        serviceCase.Tasks.Add(new ChecklistTask
        {
            CaseId = serviceCase.Id,
            Order = serviceCase.Tasks.Count + 1,
            Title = title,
            Kind = kind,
            Source = source,
            SourceId = sourceId,
            CategoryId = categoryId,
            RequiresApproval = requiresApproval,
            Step = step,
            StepTarget = stepTarget,
        });
}
