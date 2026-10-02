using System.Text.Json;
using Giim.Agent.Accounts;
using Microsoft.Extensions.Options;

namespace Giim.Agent;

/// <summary>
/// Carries out one job against the directory. Every step finds the account by employee ID first, so repeating a step
/// (after a lost connection, say) does no harm. In a dry run it only describes what it would do.
/// </summary>
internal sealed class JobRunner(IDirectory directory, IOptions<AgentOptions> agent, IOptions<AccountRules> rules)
{
    private sealed record Facts(string EmployeeId, string DisplayName, string? GivenName, string? Surname, string? Department,
        string? DepartmentCode, string? JobTitle, string? Location, string? ManagerUserPrincipalName, string? Group);

    public async Task<JobOutcome> RunAsync(AgentJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var dryRun = job.DryRun || agent.Value.DryRun;
        try
        {
            var facts = JsonSerializer.Deserialize<Facts>(job.Parameters, JsonSerializerOptions.Web)
                ?? throw new DirectoryException("The job has no parameters.", retryable: false);
            return job.Step switch
            {
                "CreateAccount" => await CreateAccountAsync(facts, dryRun, cancellationToken),
                "EnableRemoteMailbox" => await OnAccountAsync(facts, dryRun,
                    u => u.RemoteMailbox ? null : $"enable a remote mailbox for {u.SamAccountName} routed to {Routing(u)}",
                    u => directory.EnableRemoteMailboxAsync(u, Routing(u), cancellationToken), cancellationToken),
                // A group the AD administrators haven't allowed (or a privileged one) is refused before anything else.
                "AddToGroup" when rules.Value.GroupRefusal(Required(facts.Group, "group")) is { } refusal =>
                    new JobOutcome(false, null, refusal, null, Retryable: false),
                "AddToGroup" => await OnAccountAsync(facts, dryRun,
                    u => u.MemberOf.Contains(facts.Group!, StringComparer.OrdinalIgnoreCase) ? null : $"add {u.SamAccountName} to {facts.Group}",
                    u => directory.AddToGroupAsync(u, Required(facts.Group, "group"), cancellationToken), cancellationToken),
                "EnableAccount" => await OnAccountAsync(facts, dryRun,
                    u => u.Enabled ? null : $"enable {u.SamAccountName}",
                    u => directory.EnableAsync(u, cancellationToken), cancellationToken),
                _ => new JobOutcome(false, null, $"This agent doesn't know how to do {job.Step}.", null, Retryable: false),
            };
        }
        catch (DirectoryException e)
        {
            return new JobOutcome(false, null, e.Message, null, e.Retryable);
        }
        catch (JsonException)
        {
            return new JobOutcome(false, null, "The job's parameters couldn't be read.", null, Retryable: false);
        }
    }

    private async Task<JobOutcome> CreateAccountAsync(Facts f, bool dryRun, CancellationToken cancellationToken)
    {
        if (await directory.FindByEmployeeIdAsync(f.EmployeeId, cancellationToken) is { } existing)
            return new JobOutcome(true, Result(existing), null, $"{existing.SamAccountName} already exists for employee {f.EmployeeId}; nothing to do", false);

        var r = rules.Value;
        var baseName = AccountNaming.Base(r.NameFormat, f.GivenName ?? f.DisplayName, f.Surname ?? "");
        var sam = await AccountNaming.FreeAsync(baseName, name => directory.SamAccountNameTakenAsync(name, cancellationToken));
        var upn = $"{sam}@{r.UpnSuffix}";
        var ou = r.OuFor(f.DepartmentCode);
        var what = $"create {sam} ({upn}) for {f.DisplayName}, employee {f.EmployeeId}, in {ou}, disabled until the start date";
        if (dryRun) return new JobOutcome(true, null, null, $"Dry run: would {what}", false);

        var created = await directory.CreateUserAsync(new NewDirectoryUser(f.EmployeeId, sam, upn, f.DisplayName, f.GivenName ?? f.DisplayName,
            f.Surname ?? "", f.Department, f.JobTitle, f.Location, f.ManagerUserPrincipalName, ou), cancellationToken);
        return new JobOutcome(true, Result(created), null, $"Created {created.SamAccountName} ({created.UserPrincipalName}) in {ou}, disabled", false);
    }

    /// <summary>A step on an existing account: <paramref name="todo"/> says what's needed (null: already done).</summary>
    private async Task<JobOutcome> OnAccountAsync(Facts f, bool dryRun, Func<DirectoryUser, string?> todo,
        Func<DirectoryUser, Task<DirectoryUser>> act, CancellationToken cancellationToken)
    {
        var user = await directory.FindByEmployeeIdAsync(f.EmployeeId, cancellationToken);
        if (user is null)
        {
            // In a dry run the account step didn't really happen; say what would follow it.
            return dryRun
                ? new JobOutcome(true, null, null, $"Dry run: would {todo(Predicted(f)) ?? "do nothing"} (once the account exists)", false)
                // The account may not have reached this domain controller yet: worth trying again.
                : new JobOutcome(false, null, $"No AD account for employee {f.EmployeeId} yet.", null, Retryable: true);
        }

        var needed = todo(user);
        if (needed is null) return new JobOutcome(true, Result(user), null, $"Already done for {user.SamAccountName}; nothing to do", false);
        if (dryRun) return new JobOutcome(true, null, null, $"Dry run: would {needed}", false);

        var after = await act(user);
        return new JobOutcome(true, Result(after), null, $"Done: {needed}", false);
    }

    private string Routing(DirectoryUser u) => $"{u.SamAccountName}@{rules.Value.RemoteRoutingDomain}";

    /// <summary>In a dry run the account doesn't exist: the name it would get by the naming rules (before any clash number).</summary>
    private DirectoryUser Predicted(Facts f)
    {
        var sam = AccountNaming.Base(rules.Value.NameFormat, f.GivenName ?? f.DisplayName, f.Surname ?? "");
        return new(f.EmployeeId, sam, $"{sam}@{rules.Value.UpnSuffix}", Guid.Empty, "", false, false, null, []);
    }

    /// <summary>What GIIM records: the account's names and GUID (never a password; there isn't one to send).</summary>
    private static object Result(DirectoryUser u) => new
    {
        u.SamAccountName, u.UserPrincipalName, u.ObjectGuid, u.DistinguishedName, u.Mail, u.Enabled, u.RemoteMailbox,
    };

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new DirectoryException($"The job doesn't say which {what}.", retryable: false) : value;
}
