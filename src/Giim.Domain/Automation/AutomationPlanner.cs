using System.Text.Json;
using Giim.Domain.Cases;
using Giim.Domain.Common;

namespace Giim.Domain.Automation;

/// <summary>What the agent needs to create a starter's account. Never includes a password.</summary>
public sealed record AccountFacts(
    string EmployeeId,
    string DisplayName,
    string GivenName,
    string Surname,
    string? Department,
    string? DepartmentCode,
    string? JobTitle,
    string? Location,
    string? ManagerUserPrincipalName,
    DateOnly? StartDate,
    string Track);

/// <summary>One job the planner would queue, for the preview and for queueing.</summary>
public sealed record PlannedStep(ChecklistTask Task, AutomationJob Job, string Description);

/// <summary>
/// Turns a starter checklist into automation jobs in a safe order: the account first; the mailbox, groups and the
/// cloud-sync wait after it; enabling the account on the start date; the welcome email last.
/// </summary>
public static class AutomationPlanner
{
    public static IReadOnlyList<PlannedStep> Plan(ServiceCase serviceCase, AccountFacts facts, IReadOnlySet<Guid> tasksWithActiveJobs,
        DateTimeOffset enableAt, DateTimeOffset now, bool dryRun, string createdBy)
    {
        ArgumentNullException.ThrowIfNull(serviceCase);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(tasksWithActiveJobs);
        if (serviceCase.Type != CaseType.Onboarding)
            throw new DomainException("Only starter checklists can be automated so far.");
        if (serviceCase.IsClosed)
            throw new DomainException($"This checklist is {serviceCase.Status.ToString().ToLowerInvariant()}.");

        var tasks = serviceCase.Tasks
            .Where(t => t.Step is not null && t.Status is TaskState.Pending or TaskState.Failed && !tasksWithActiveJobs.Contains(t.Id))
            .OrderBy(t => Order(t.Step!.Value)).ThenBy(t => t.Order)
            .ToList();
        if (tasks.Count == 0) return [];

        var planned = new List<PlannedStep>();
        AutomationJob? account = null, cloudSync = null, enable = null;
        foreach (var task in tasks)
        {
            var step = task.Step!.Value;
            if (step is AutomationStep.AddToGroup or AutomationStep.AddToCloudGroup && string.IsNullOrWhiteSpace(task.StepTarget)) continue;

            var after = step switch
            {
                AutomationStep.CreateAccount => null,
                AutomationStep.SendWelcomeEmail => enable ?? account,
                // Graph can only add the account once Entra Connect has synced it.
                AutomationStep.AddToCloudGroup => cloudSync ?? account,
                _ => account,
            };
            // A dry run reports every step straight away (that's its point); a real run waits for the start date.
            var onStartDate = step is AutomationStep.EnableAccount or AutomationStep.SendWelcomeEmail && enableAt > now;
            var waitsForStart = onStartDate && !dryRun;
            var job = new AutomationJob
            {
                CaseId = serviceCase.Id,
                TaskId = task.Id,
                PersonId = serviceCase.PersonId,
                Step = step,
                Runner = AutomationJob.RunnerFor(step),
                ParametersJson = Parameters(step, facts, task.StepTarget),
                DependsOnJobId = after?.Id,
                DryRun = dryRun,
                CreatedBy = createdBy,
                CreatedAt = now,
            };
            if (waitsForStart) job.Delay(enableAt);
            if (step == AutomationStep.CreateAccount) account = job;
            if (step == AutomationStep.EnableAccount) enable = job;
            if (step == AutomationStep.WaitForCloudSync) cloudSync = job;
            planned.Add(new PlannedStep(task, job, Describe(step, facts, task.StepTarget, onStartDate ? enableAt : null)));
        }
        return planned;
    }

    private static int Order(AutomationStep step) => step switch
    {
        AutomationStep.CreateAccount => 0,
        AutomationStep.WaitForCloudSync => 1,
        AutomationStep.EnableRemoteMailbox => 2,
        AutomationStep.AddToGroup or AutomationStep.AddToCloudGroup => 3,
        AutomationStep.EnableAccount => 4,
        _ => 5,
    };

    private static string Parameters(AutomationStep step, AccountFacts f, string? target) => JsonSerializer.Serialize<object>(step switch
    {
        AutomationStep.CreateAccount => f,
        // Every step carries the name, so the agent can say which account it means even in a dry run (when none exists).
        AutomationStep.AddToGroup or AutomationStep.AddToCloudGroup => new { f.EmployeeId, f.DisplayName, f.GivenName, f.Surname, Group = target },
        _ => new { f.EmployeeId, f.DisplayName, f.GivenName, f.Surname },
    }, JsonSerializerOptions.Web);

    private static string Describe(AutomationStep step, AccountFacts f, string? target, DateTimeOffset? notBefore) => step switch
    {
        AutomationStep.CreateAccount => $"Create a disabled AD account for {f.DisplayName} ({f.EmployeeId})"
            + (f.Department is null ? "" : $" in {f.Department}") + "; the agent chooses the username and OU by its own rules",
        AutomationStep.WaitForCloudSync => "Wait until Entra Connect has synced the account to Microsoft 365",
        AutomationStep.EnableRemoteMailbox => "Enable the remote mailbox (hybrid Exchange)",
        AutomationStep.AddToGroup => $"Add to the AD group {target}",
        AutomationStep.AddToCloudGroup => $"Add to the cloud-only Entra group {target} (GIIM, through Graph, once synced)",
        AutomationStep.EnableAccount => "Enable the account" + (notBefore is { } at ? $" from {at:ddd d MMM HH:mm}" : " straight away (the start date has passed)"),
        _ => "Email the manager that their starter is set up" + (notBefore is { } on ? $", on {on:ddd d MMM}" : ""),
    };
}
