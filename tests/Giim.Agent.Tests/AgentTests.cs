using System.Text.Json;
using Giim.Agent;
using Giim.Agent.Accounts;
using Microsoft.Extensions.Options;

namespace Giim.Agent.Tests;

public class AccountNamingTests
{
    [Theory]
    [InlineData("{first}.{last}", "Priya", "Patel", "priya.patel")]
    [InlineData("{first}.{last}", "Zoë", "Ng", "zoe.ng")]
    [InlineData("{first}.{last}", "Mary Ann", "O'Brien-Smith", "mary.obrien-smith")]
    [InlineData("{f}{last}", "Priya", "Patel", "ppatel")]
    [InlineData("{first}.{last}", "Bartholomew", "Featherstonehaugh", "bartholomew.feathers")]
    public void Names_follow_the_format_and_fit_ad(string format, string first, string last, string expected)
    {
        var name = AccountNaming.Base(format, first, last);

        Assert.Equal(expected, name);
        Assert.True(name.Length <= AccountNaming.MaxSamLength);
    }

    [Fact]
    public async Task A_taken_name_gets_a_number_and_still_fits()
    {
        var taken = new HashSet<string> { "priya.patel", "priya.patel2" };
        Assert.Equal("priya.patel3", await AccountNaming.FreeAsync("priya.patel", n => Task.FromResult(taken.Contains(n))));

        var longName = "bartholomew.feathers";
        Assert.Equal("bartholomew.feather2", await AccountNaming.FreeAsync(longName, n => Task.FromResult(n == longName)));
    }

    [Fact]
    public void A_name_with_nothing_usable_is_refused()
    {
        Assert.Throws<DirectoryException>(() => AccountNaming.Base("{first}.{last}", "李", "王"));
    }
}

public sealed class JobRunnerTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"giim-standin-{Guid.NewGuid():N}.json");
    private readonly StandInDirectory _directory;
    private readonly AccountRules _rules = new()
    {
        UpnSuffix = "contoso.example",
        DefaultOu = "OU=New Starters,DC=contoso,DC=example",
        DepartmentOus = new(StringComparer.OrdinalIgnoreCase) { ["FIN"] = "OU=Finance,DC=contoso,DC=example" },
    };

    public JobRunnerTests() => _directory = new StandInDirectory(Options.Create(new AgentOptions { StandInPath = _file }), TimeProvider.System);

    public void Dispose()
    {
        _directory.Dispose();
        if (File.Exists(_file)) File.Delete(_file);
    }

    private JobRunner Runner(bool agentDryRun = false) =>
        new(_directory, Options.Create(new AgentOptions { StandInPath = _file, DryRun = agentDryRun }), Options.Create(_rules));

    private static AgentJob Job(string step, string employeeId = "E1001", string? group = null, bool dryRun = false) =>
        new(Guid.NewGuid(), step, JsonSerializer.Serialize(new
        {
            employeeId, displayName = "Priya Patel", givenName = "Priya", surname = "Patel", department = "Finance",
            departmentCode = "FIN", jobTitle = "Accountant", location = "Sydney", managerUserPrincipalName = "boss@contoso.example", group,
        }, JsonSerializerOptions.Web), dryRun, 1);

    private static JsonElement Result(JobOutcome outcome) => JsonSerializer.SerializeToElement(outcome.Result, JsonSerializerOptions.Web);

    [Fact]
    public async Task Creates_a_disabled_account_in_the_departments_ou()
    {
        var outcome = await Runner().RunAsync(Job("CreateAccount"), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = Result(outcome);
        Assert.Equal("priya.patel@contoso.example", result.GetProperty("userPrincipalName").GetString());
        Assert.False(result.GetProperty("enabled").GetBoolean());
        Assert.Contains("OU=Finance", result.GetProperty("distinguishedName").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("password", JsonSerializer.Serialize(outcome), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creating_again_finds_the_same_account()
    {
        var first = Result(await Runner().RunAsync(Job("CreateAccount"), CancellationToken.None));

        var again = await Runner().RunAsync(Job("CreateAccount"), CancellationToken.None);

        Assert.True(again.Succeeded);
        Assert.Contains("already exists", again.Log, StringComparison.Ordinal);
        Assert.Equal(first.GetProperty("objectGuid").GetGuid(), Result(again).GetProperty("objectGuid").GetGuid());
    }

    [Fact]
    public async Task A_second_person_with_the_same_name_gets_a_numbered_name()
    {
        await Runner().RunAsync(Job("CreateAccount", "E1001"), CancellationToken.None);

        var other = await Runner().RunAsync(Job("CreateAccount", "E2002"), CancellationToken.None);

        Assert.Equal("priya.patel2@contoso.example", Result(other).GetProperty("userPrincipalName").GetString());
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_and_says_what_it_would_do()
    {
        var outcome = await Runner().RunAsync(Job("CreateAccount", dryRun: true), CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.StartsWith("Dry run: would create priya.patel", outcome.Log, StringComparison.Ordinal);
        Assert.Null(await _directory.FindByEmployeeIdAsync("E1001", CancellationToken.None));
        var group = await Runner().RunAsync(Job("AddToGroup", group: "SG-Finance", dryRun: true), CancellationToken.None);
        Assert.True(group.Succeeded);
        Assert.Equal("Dry run: would add priya.patel to SG-Finance (once the account exists)", group.Log);
    }

    [Fact]
    public async Task The_agents_own_dry_run_setting_overrides_giim()
    {
        var outcome = await Runner(agentDryRun: true).RunAsync(Job("CreateAccount", dryRun: false), CancellationToken.None);

        Assert.StartsWith("Dry run", outcome.Log, StringComparison.Ordinal);
        Assert.Null(await _directory.FindByEmployeeIdAsync("E1001", CancellationToken.None));
    }

    [Fact]
    public async Task Mailbox_groups_and_enabling_act_on_the_account_once()
    {
        await Runner().RunAsync(Job("CreateAccount"), CancellationToken.None);

        Assert.True((await Runner().RunAsync(Job("EnableRemoteMailbox"), CancellationToken.None)).Succeeded);
        Assert.True((await Runner().RunAsync(Job("AddToGroup", group: "SG-Finance"), CancellationToken.None)).Succeeded);
        var again = await Runner().RunAsync(Job("AddToGroup", group: "SG-Finance"), CancellationToken.None);
        Assert.Contains("nothing to do", again.Log, StringComparison.Ordinal);
        Assert.True((await Runner().RunAsync(Job("EnableAccount"), CancellationToken.None)).Succeeded);

        var user = await _directory.FindByEmployeeIdAsync("E1001", CancellationToken.None);
        Assert.True(user!.Enabled && user.RemoteMailbox);
        Assert.Equal(["SG-Finance"], user.MemberOf);
    }

    [Fact]
    public async Task A_missing_group_fails_for_a_person_to_fix()
    {
        await Runner().RunAsync(Job("CreateAccount"), CancellationToken.None);

        var outcome = await Runner().RunAsync(Job("AddToGroup", group: "MISSING-Group"), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Retryable);
        Assert.Contains("no AD group", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_before_the_account_exists_is_retried()
    {
        var outcome = await Runner().RunAsync(Job("EnableAccount"), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.Retryable);
    }

    [Fact]
    public async Task Unknown_steps_are_refused()
    {
        var outcome = await Runner().RunAsync(Job("DeleteEverything"), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Retryable);
    }
}

public class GroupRulesTests
{
    [Theory]
    [InlineData("Domain Admins")]
    [InlineData("enterprise admins")]
    [InlineData("Administrators")]
    public void Privileged_groups_are_always_refused(string group)
    {
        Assert.NotNull(new AccountRules().GroupRefusal(group));
        Assert.NotNull(new AccountRules { AllowedGroups = ["*"] }.GroupRefusal(group));
    }

    [Fact]
    public void With_an_allow_list_only_those_groups_are_added()
    {
        var rules = new AccountRules { AllowedGroups = ["APP-*", "LIC-M365-E3"] };

        Assert.Null(rules.GroupRefusal("APP-Jira"));
        Assert.Null(rules.GroupRefusal("lic-m365-e3"));
        Assert.NotNull(rules.GroupRefusal("LIC-M365-E5"));
        Assert.NotNull(rules.GroupRefusal("SG-Finance"));
    }

    [Fact]
    public async Task A_refused_group_fails_the_step_before_touching_the_directory()
    {
        var file = Path.Combine(Path.GetTempPath(), $"giim-standin-{Guid.NewGuid():N}.json");
        using var directory = new StandInDirectory(Options.Create(new AgentOptions { StandInPath = file }), TimeProvider.System);
        var runner = new JobRunner(directory, Options.Create(new AgentOptions { StandInPath = file }),
            Options.Create(new AccountRules { AllowedGroups = ["APP-*"] }));
        var job = new AgentJob(Guid.NewGuid(), "AddToGroup",
            JsonSerializer.Serialize(new { employeeId = "E1", displayName = "Sam Lee", group = "Domain Admins" }, JsonSerializerOptions.Web), false, 1);

        var outcome = await runner.RunAsync(job, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Retryable);
        Assert.Contains("privileged", outcome.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(file));
    }
}

public class AgentVersionTests
{
    [Theory]
    [InlineData("1.0.0+6d48af8dad75c00741ad763a22a84a5cc2970abc", "1.0.0 (6d48af8)")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(null, "dev")]
    public void The_version_shown_in_giim_is_short(string? informational, string expected)
    {
        Assert.Equal(expected, AgentWorker.ShortVersion(informational));
    }
}
