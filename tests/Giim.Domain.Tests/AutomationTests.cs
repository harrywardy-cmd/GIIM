using System.Text.Json;
using Giim.Domain.Automation;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Domain.Provisioning;

namespace Giim.Domain.Tests;

public class AutomationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(10));
    private static readonly DateTimeOffset StartMorning = new(2026, 10, 12, 6, 0, 0, TimeSpan.FromHours(11));
    private static readonly AccountFacts Facts = new("E1001", "Priya Patel", "Priya", "Patel", "Finance", "FIN", "Accountant",
        "Sydney", "boss@contoso.com", new DateOnly(2026, 10, 12), "Full");

    private static ServiceCase Starter(ProvisioningTrack track = ProvisioningTrack.Full)
    {
        var profile = new RoleProfile { Name = "Finance" };
        profile.Items.AddRange([
            new ProfileItem { Type = ProfileItemType.Hardware, Description = "Standard laptop" },
            new ProfileItem { Type = ProfileItemType.Application, Description = "Xero", GroupName = "APP-Xero-Users" },
            new ProfileItem { Type = ProfileItemType.Application, Description = "Paper forms" },
            new ProfileItem { Type = ProfileItemType.SecurityGroup, Description = "Finance staff", GroupName = "SG-Finance" },
            new ProfileItem { Type = ProfileItemType.LicenceGroup, Description = "M365 E3", GroupName = "LIC-M365-E3" },
        ]);
        var person = new Person { EmployeeId = "E1001", DisplayName = "Priya Patel", Track = track, StartDate = new DateOnly(2026, 10, 12) };
        return ChecklistGenerator.ForOnboarding(person, profile, "5601");
    }

    private static IReadOnlyList<PlannedStep> Plan(ServiceCase c, IReadOnlySet<Guid>? active = null, DateTimeOffset? enableAt = null) =>
        AutomationPlanner.Plan(c, Facts, active ?? new HashSet<Guid>(), enableAt ?? StartMorning, Now, dryRun: false, "jane.tech");

    // ---- Checklists know which tasks can be automated ------------------------------------------------------------------

    [Fact]
    public void Starter_checklists_mark_the_steps_the_agent_can_do()
    {
        var c = Starter();

        AutomationStep? StepOf(string title) => c.Tasks.Single(t => t.Title == title).Step;
        Assert.Equal(AutomationStep.CreateAccount, StepOf("Create AD account"));
        Assert.Equal(AutomationStep.EnableRemoteMailbox, StepOf("Enable remote mailbox (hybrid Exchange)"));
        Assert.Equal(AutomationStep.EnableAccount, StepOf("Enable the account on the start date"));
        var xero = c.Tasks.Single(t => t.Title == "Grant app: Xero");
        Assert.Equal((AutomationStep.AddToGroup, "APP-Xero-Users"), (xero.Step, xero.StepTarget));
        Assert.Null(StepOf("Grant app: Paper forms"));        // no access group: done by hand
        Assert.Null(StepOf("Allocate and scan: Standard laptop"));
    }

    // ---- The planner -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_account_comes_first_and_everything_else_waits_for_it()
    {
        var plan = Plan(Starter());

        var account = plan[0].Job;
        Assert.Equal(AutomationStep.CreateAccount, account.Step);
        Assert.Null(account.DependsOnJobId);
        Assert.All(plan.Skip(1).Where(p => p.Job.Step != AutomationStep.SendWelcomeEmail), p => Assert.Equal(account.Id, p.Job.DependsOnJobId));
        Assert.Equal("APP-Xero-Users,LIC-M365-E3,SG-Finance",
            string.Join(',', plan.Where(p => p.Job.Step == AutomationStep.AddToGroup).Select(p => p.Task.StepTarget!).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Enabling_and_the_welcome_email_wait_for_the_start_date()
    {
        var plan = Plan(Starter());

        var enable = plan.Single(p => p.Job.Step == AutomationStep.EnableAccount).Job;
        var email = plan.Single(p => p.Job.Step == AutomationStep.SendWelcomeEmail).Job;
        Assert.Equal(StartMorning, enable.NotBefore);
        Assert.Equal(enable.Id, email.DependsOnJobId);
        Assert.Equal(StartMorning, email.NotBefore);
        Assert.Null(plan.Single(p => p.Job.Step == AutomationStep.CreateAccount).Job.NotBefore);
    }

    [Fact]
    public void A_cloud_only_group_is_added_by_giim_after_the_account_has_synced()
    {
        var profile = new RoleProfile { Name = "Finance" };
        profile.Items.Add(new ProfileItem { Type = ProfileItemType.SecurityGroup, Description = "Teams: Finance", GroupName = "Finance Team", CloudGroup = true });
        var person = new Person { EmployeeId = "E1001", DisplayName = "Priya Patel", StartDate = new DateOnly(2026, 10, 12) };

        var plan = AutomationPlanner.Plan(ChecklistGenerator.ForOnboarding(person, profile, null), Facts, new HashSet<Guid>(), StartMorning, Now,
            dryRun: false, "jane.tech");

        var sync = plan.Single(p => p.Job.Step == AutomationStep.WaitForCloudSync).Job;
        var cloud = plan.Single(p => p.Job.Step == AutomationStep.AddToCloudGroup);
        Assert.Equal((AutomationRunner.Giim, sync.Id, "Finance Team"), (cloud.Job.Runner, cloud.Job.DependsOnJobId, cloud.Task.StepTarget));
        Assert.Contains("cloud-only Entra group", cloud.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_reports_every_step_at_once_but_still_says_when_it_would_happen()
    {
        var plan = AutomationPlanner.Plan(Starter(), Facts, new HashSet<Guid>(), StartMorning, Now, dryRun: true, "jane.tech");

        var enable = plan.Single(p => p.Job.Step == AutomationStep.EnableAccount);
        Assert.Null(enable.Job.NotBefore);
        Assert.True(enable.Job.DryRun);
        Assert.Contains("12 Oct", enable.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_start_date_already_passed_enables_straight_away()
    {
        var plan = Plan(Starter(), enableAt: Now.AddDays(-1));

        Assert.Null(plan.Single(p => p.Job.Step == AutomationStep.EnableAccount).Job.NotBefore);
    }

    [Fact]
    public void Steps_run_by_giim_and_by_the_agent_are_told_apart()
    {
        var runners = Plan(Starter()).ToDictionary(p => p.Job.Step == AutomationStep.AddToGroup ? $"group {p.Task.StepTarget}" : p.Job.Step.ToString(), p => p.Job.Runner);

        Assert.Equal(AutomationRunner.Agent, runners["CreateAccount"]);
        Assert.Equal(AutomationRunner.Giim, runners["WaitForCloudSync"]);
        Assert.Equal(AutomationRunner.Giim, runners["SendWelcomeEmail"]);
    }

    [Fact]
    public void The_account_job_carries_the_facts_and_never_a_password()
    {
        var job = Plan(Starter()).Single(p => p.Job.Step == AutomationStep.CreateAccount).Job;
        var json = JsonDocument.Parse(job.ParametersJson).RootElement;

        Assert.Equal("E1001", json.GetProperty("employeeId").GetString());
        Assert.Equal("Patel", json.GetProperty("surname").GetString());
        Assert.Equal("boss@contoso.com", json.GetProperty("managerUserPrincipalName").GetString());
        Assert.DoesNotContain("password", job.ParametersJson, StringComparison.OrdinalIgnoreCase);
        var group = Plan(Starter()).First(p => p.Job.Step == AutomationStep.AddToGroup).Job;
        Assert.False(string.IsNullOrEmpty(JsonDocument.Parse(group.ParametersJson).RootElement.GetProperty("group").GetString()));
    }

    [Fact]
    public void Done_and_already_running_steps_are_not_queued_again()
    {
        var c = Starter();
        var account = c.Tasks.Single(t => t.Step == AutomationStep.CreateAccount);
        var mailbox = c.Tasks.Single(t => t.Step == AutomationStep.EnableRemoteMailbox);
        c.CompleteTask(account.Id, "jane.tech", "Created by hand", Now);

        var plan = Plan(c, active: new HashSet<Guid> { mailbox.Id });

        Assert.DoesNotContain(plan, p => p.Task.Id == account.Id || p.Task.Id == mailbox.Id);
        Assert.All(plan.Where(p => p.Job.Step == AutomationStep.AddToGroup), p => Assert.Null(p.Job.DependsOnJobId));   // the account already exists
    }

    [Fact]
    public void Light_track_starters_have_no_mailbox_step()
    {
        Assert.DoesNotContain(Plan(Starter(ProvisioningTrack.Light)), p => p.Job.Step == AutomationStep.EnableRemoteMailbox);
    }

    [Fact]
    public void Only_open_starter_checklists_can_be_automated()
    {
        var person = new Person { EmployeeId = "E2", DisplayName = "Leaving Person" };
        var leaver = ChecklistGenerator.ForOffboarding(person, [], [], null);
        Assert.Throws<DomainException>(() => Plan(leaver));

        var cancelled = Starter();
        cancelled.Cancel("jane.tech", "Withdrew", Now);
        Assert.Throws<DomainException>(() => Plan(cancelled));
    }

    // ---- Jobs ----------------------------------------------------------------------------------------------------------

    private static AutomationJob Job(DateTimeOffset? notBefore = null)
    {
        var job = new AutomationJob { Step = AutomationStep.CreateAccount, Runner = AutomationRunner.Agent, ParametersJson = "{}", CreatedBy = "jane.tech" };
        if (notBefore is { } at) job.Delay(at);
        return job;
    }

    [Fact]
    public void A_job_waits_for_its_dependency_and_its_time()
    {
        Assert.False(Job().CanBeClaimed(Now, dependencySucceeded: false));
        Assert.False(Job(Now.AddHours(1)).CanBeClaimed(Now, dependencySucceeded: true));
        Assert.True(Job(Now.AddHours(-1)).CanBeClaimed(Now, dependencySucceeded: true));
    }

    [Fact]
    public void A_claim_that_lapses_lets_another_runner_take_the_job()
    {
        var job = Job();
        job.Claim("AGENT-01", Now, TimeSpan.FromMinutes(10));

        Assert.False(job.CanBeClaimed(Now.AddMinutes(5), true));
        Assert.True(job.CanBeClaimed(Now.AddMinutes(11), true));
        job.Claim("AGENT-02", Now.AddMinutes(11), TimeSpan.FromMinutes(10));
        Assert.Throws<DomainException>(() => job.Succeed("AGENT-01", null, null, Now.AddMinutes(12)));   // its claim lapsed
        job.Succeed("AGENT-02", """{"userPrincipalName":"priya.patel@contoso.com"}""", "Created", Now.AddMinutes(12));
        Assert.Equal((JobStatus.Succeeded, 2), (job.Status, job.Attempts));
    }

    [Fact]
    public void Temporary_failures_retry_with_a_growing_delay_then_stop()
    {
        var job = Job();
        var at = Now;
        for (var attempt = 1; attempt <= AutomationJob.DefaultMaxAttempts; attempt++)
        {
            job.Claim("AGENT-01", at, TimeSpan.FromMinutes(10));
            var retrying = job.Fail("AGENT-01", "Domain controller didn't answer", null, retryable: true, at);
            Assert.Equal(attempt < AutomationJob.DefaultMaxAttempts, retrying);
            if (retrying) Assert.True(job.NotBefore > at);
            at = job.NotBefore ?? at;
        }
        Assert.Equal(JobStatus.Failed, job.Status);

        job.Retry(at);
        Assert.Equal((JobStatus.Queued, 0, null), (job.Status, job.Attempts, job.Error));
    }

    [Fact]
    public void A_permanent_failure_stops_at_once()
    {
        var job = Job();
        job.Claim("AGENT-01", Now, TimeSpan.FromMinutes(10));

        Assert.False(job.Fail("AGENT-01", "Group APP-X doesn't exist", "log", retryable: false, Now));
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public void A_finished_job_cannot_be_cancelled_and_only_failed_ones_retried()
    {
        var job = Job();
        Assert.Throws<DomainException>(() => job.Retry(Now));
        job.Claim("AGENT-01", Now, TimeSpan.FromMinutes(10));
        job.Succeed("AGENT-01", null, null, Now);
        Assert.Throws<DomainException>(() => job.Cancel("jane.tech", Now));
    }

    // ---- Checklist tasks follow their jobs -------------------------------------------------------------------------------

    [Fact]
    public void Automated_tasks_show_running_waiting_failed_or_back_with_a_person()
    {
        var c = Starter();
        var account = c.Tasks.Single(t => t.Step == AutomationStep.CreateAccount);
        var enable = c.Tasks.Single(t => t.Step == AutomationStep.EnableAccount);
        var laptop = c.Tasks.Single(t => t.Title.StartsWith("Allocate", StringComparison.Ordinal));

        c.StartAutomatedTask(account.Id, waitingForDate: false, "Queued", Now);
        c.StartAutomatedTask(enable.Id, waitingForDate: true, "Queued for the start date", Now);
        Assert.Equal((TaskState.Running, TaskState.Waiting, CaseStatus.InProgress), (account.Status, enable.Status, c.Status));
        Assert.Throws<DomainException>(() => c.StartAutomatedTask(laptop.Id, false, "x", Now));

        c.FailAutomatedTask(account.Id, "Name clash", Now);
        Assert.Equal(TaskState.Failed, account.Status);
        c.ReturnTaskToPerson(account.Id, "Automation stopped by jane.tech", Now);
        Assert.Equal(TaskState.Pending, account.Status);
    }

    [Theory]
    [InlineData("Priya Patel", "Priya", "Patel")]
    [InlineData("Mary Ann Smith", "Mary Ann", "Smith")]
    [InlineData("Patel, Priya", "Priya", "Patel")]
    [InlineData("  Cher  ", "Cher", "")]
    public void Names_split_into_first_name_and_surname(string display, string given, string surname)
    {
        Assert.Equal((given, surname), PersonName.Split(display));
    }
}
