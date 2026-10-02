using System.Text.Json;
using Giim.Connectors.CloudAccounts;
using Giim.Domain.Automation;
using Giim.Domain.Cases;
using Giim.Domain.Common;
using Giim.Domain.People;
using Giim.Infrastructure.Notifications;
using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Giim.Infrastructure.Automation;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    /// <summary>Jobs only report what they would do. On until the AD administrators are happy with the agent's rules.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>When on the start date the account is enabled (local time).</summary>
    public TimeOnly EnableAt { get; set; } = new(6, 0);

    public string TimeZone { get; set; } = "Australia/Sydney";

    /// <summary>How long a runner holds a job before it is handed to another.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often GIIM looks for the new account in Entra ID, and how long it waits before giving up.</summary>
    public TimeSpan CloudSyncCheckEvery { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan CloudSyncTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>An agent that hasn't checked in for this long is shown as offline.</summary>
    public TimeSpan AgentOfflineAfter { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed record AgentView(string Name, DateTimeOffset LastSeenAt, bool Online, string? Version, string? Directory, bool DryRun);

public sealed record AutomationStepView(Guid TaskId, string Task, AutomationStep Step, string Description, Guid? JobId, JobStatus? Status,
    AutomationRunner Runner, string? ClaimedBy, DateTimeOffset? NotBefore, int Attempts, string? Error, string? Log, bool DryRun,
    DateTimeOffset? CompletedAt, bool CanRetry, bool CanStop);

/// <summary>A checklist's automation: what has run, what would run, and whether an agent is there to do it.</summary>
public sealed record CaseAutomation(bool DryRun, bool AgentOnline, IReadOnlyList<AgentView> Agents, IReadOnlyList<AutomationStepView> Steps,
    int ReadyToStart, string? CannotStart);

/// <summary>A job handed to a runner. Parameters are the JSON fixed when it was queued.</summary>
public sealed record ClaimedJob(Guid Id, AutomationStep Step, string Parameters, bool DryRun, int Attempt);

public sealed record JobResult(bool Succeeded, string? ResultJson, string? Error, string? Log, bool Retryable);

public sealed record AutomationOverview(bool DryRun, IReadOnlyList<AgentView> Agents, int Queued, int Running, int Waiting,
    IReadOnlyList<FailedJobView> RecentFailures);

public sealed record FailedJobView(Guid JobId, Guid CaseId, string Person, AutomationStep Step, string? Error, DateTimeOffset? At);

/// <summary>
/// Runs starter checklist steps automatically. A technician starts it for a checklist; jobs are queued in order; the
/// on-prem agent (AD and Exchange steps) and GIIM itself (waiting for cloud sync, the welcome email) claim and run
/// them; results tick the checklist tasks off, or mark them failed for a person to retry or do by hand.
/// </summary>
public sealed class AutomationService(GiimDbContext db, ICloudDirectory cloud, IOptions<AutomationOptions> options,
    IOptions<GiimOptions> giim, TimeProvider clock)
{
    public const string GiimRunner = "GIIM";
    private AutomationOptions O => options.Value;

    // ---- For people (the checklist page and Setup) --------------------------------------------------------------------

    public async Task<CaseAutomation> GetAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var serviceCase = await LoadCaseAsync(caseId, tracked: false, cancellationToken);
        var jobs = await db.AutomationJobs.AsNoTracking().Where(j => j.CaseId == caseId).ToListAsync(cancellationToken);
        var latest = jobs.GroupBy(j => j.TaskId).ToDictionary(g => g.Key, g => g.MaxBy(j => j.CreatedAt)!);
        var active = latest.Values.Where(j => !j.IsFinished).Select(j => j.TaskId).ToHashSet();

        IReadOnlyList<PlannedStep> plan = [];
        string? cannotStart = null;
        try
        {
            plan = await PlanAsync(serviceCase, active, now, cancellationToken);
        }
        catch (DomainException e)
        {
            cannotStart = e.Message;
        }
        var planned = plan.ToDictionary(p => p.Task.Id);

        var steps = serviceCase.Tasks.Where(t => t.Step is not null).OrderBy(t => t.Order).Select(t =>
        {
            var job = latest.GetValueOrDefault(t.Id);
            var description = planned.TryGetValue(t.Id, out var p) ? p.Description : t.Title;
            return new AutomationStepView(t.Id, t.Title, t.Step!.Value, description, job?.Id, job?.Status,
                AutomationJob.RunnerFor(t.Step.Value), job?.ClaimedBy, job?.NotBefore, job?.Attempts ?? 0, job?.Error, job?.Log,
                job?.DryRun ?? O.DryRun, job?.CompletedAt, job?.Status == JobStatus.Failed && t.Status != TaskState.Done,
                job is { IsFinished: false });
        }).ToList();

        var agents = await AgentsAsync(now, cancellationToken);
        return new CaseAutomation(O.DryRun, agents.Any(a => a.Online), agents, steps, plan.Count,
            cannotStart ?? (plan.Count == 0 ? "Nothing is waiting to be automated." : null));
    }

    public async Task<int> StartAsync(Guid caseId, string actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var serviceCase = await LoadCaseAsync(caseId, tracked: true, cancellationToken);
        var active = (await db.AutomationJobs.AsNoTracking()
            .Where(j => j.CaseId == caseId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running))
            .Select(j => j.TaskId).ToListAsync(cancellationToken)).ToHashSet();

        var plan = await PlanAsync(serviceCase, active, now, cancellationToken, actor);
        if (plan.Count == 0) throw new DomainException("Nothing is waiting to be automated on this checklist.");

        foreach (var step in plan)
        {
            db.AutomationJobs.Add(step.Job);
            serviceCase.StartAutomatedTask(step.Task.Id, waitingForDate: step.Job.NotBefore > now,
                $"Automation started by {actor}{(step.Job.DryRun ? " (dry run: nothing will be changed)" : "")}", now);
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            throw new DomainException("Someone else has just started automation on this checklist. Refresh to see it.");
        }
        return plan.Count;
    }

    /// <summary>Tries a failed step again, after the cause has been fixed.</summary>
    public async Task RetryAsync(Guid caseId, Guid jobId, string actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var job = await db.AutomationJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.CaseId == caseId, cancellationToken)
            ?? throw new KeyNotFoundException("Step not found.");
        var serviceCase = await LoadCaseAsync(caseId, tracked: true, cancellationToken);
        job.Retry(now);
        serviceCase.StartAutomatedTask(job.TaskId, waitingForDate: false, $"Retried by {actor}", now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stops a step (and the steps that wait for it), handing the tasks back to be done by hand.</summary>
    public async Task StopAsync(Guid caseId, Guid jobId, string actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var jobs = await db.AutomationJobs.Where(j => j.CaseId == caseId).ToListAsync(cancellationToken);
        var job = jobs.FirstOrDefault(j => j.Id == jobId) ?? throw new KeyNotFoundException("Step not found.");
        var serviceCase = await LoadCaseAsync(caseId, tracked: true, cancellationToken);

        var stopping = new List<AutomationJob> { job };
        for (var i = 0; i < stopping.Count; i++)
            stopping.AddRange(jobs.Where(j => j.DependsOnJobId == stopping[i].Id && !j.IsFinished && !stopping.Contains(j)));
        foreach (var j in stopping.Where(j => !j.IsFinished || j == job))
        {
            j.Cancel(actor, now);
            TryOnTask(serviceCase, j.TaskId, () => serviceCase.ReturnTaskToPerson(j.TaskId, $"Automation stopped by {actor}; do this by hand", now));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AutomationOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var counts = await db.AutomationJobs.AsNoTracking().Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .Select(j => new { j.Status, Waiting = j.NotBefore > now }).ToListAsync(cancellationToken);
        var since = now.AddDays(-14);
        var failures = await db.AutomationJobs.AsNoTracking()
            .Where(j => j.Status == JobStatus.Failed && j.CompletedAt >= since)
            .OrderByDescending(j => j.CompletedAt).Take(20)
            .Select(j => new FailedJobView(j.Id, j.CaseId, db.People.Where(p => p.Id == j.PersonId).Select(p => p.DisplayName).First(),
                j.Step, j.Error, j.CompletedAt))
            .ToListAsync(cancellationToken);
        return new AutomationOverview(O.DryRun, await AgentsAsync(now, cancellationToken),
            counts.Count(c => c.Status == JobStatus.Queued && !c.Waiting), counts.Count(c => c.Status == JobStatus.Running),
            counts.Count(c => c.Status == JobStatus.Queued && c.Waiting), failures);
    }

    // ---- For runners (the on-prem agent, and GIIM's own worker) --------------------------------------------------------

    public async Task CheckInAsync(string agent, string? version, string? directory, bool dryRun, CancellationToken cancellationToken)
    {
        var name = CleanName(agent);
        var record = await db.AgentCheckIns.FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
        if (record is null) db.AgentCheckIns.Add(record = new AgentCheckIn { Name = name });
        record.LastSeenAt = clock.GetUtcNow();
        record.Version = Short(version, 50);
        record.Directory = Short(directory, 50);
        record.DryRun = dryRun;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Hands a runner up to <paramref name="max"/> jobs that are due and whose earlier steps have succeeded.</summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(string runnerName, AutomationRunner runner, int max, CancellationToken cancellationToken)
    {
        var name = CleanName(runnerName);
        var now = clock.GetUtcNow();
        var candidates = await db.AutomationJobs
            .Where(j => j.Runner == runner && (j.Status == JobStatus.Queued && (j.NotBefore == null || j.NotBefore <= now)
                || j.Status == JobStatus.Running && j.LeaseUntil < now))
            .OrderBy(j => j.CreatedAt).Take(Math.Clamp(max, 1, 20) * 3)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var dependencyIds = candidates.Where(j => j.DependsOnJobId != null).Select(j => j.DependsOnJobId!.Value).Distinct().ToList();
        var dependencies = await db.AutomationJobs.AsNoTracking().Where(j => dependencyIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Status, cancellationToken);
        var caseIds = candidates.Select(j => j.CaseId).Distinct().ToList();
        var closedCases = await db.Cases.AsNoTracking()
            .Where(c => caseIds.Contains(c.Id) && (c.Status == CaseStatus.Cancelled || c.Status == CaseStatus.Completed))
            .Select(c => c.Id).ToListAsync(cancellationToken);

        var claimed = new List<ClaimedJob>();
        foreach (var job in candidates)
        {
            if (claimed.Count >= max) break;
            if (closedCases.Contains(job.CaseId))
            {
                // The checklist was cancelled or finished by hand: nothing left to do.
                job.Cancel("GIIM (checklist closed)", now);
                await SaveOrSkipAsync(job, cancellationToken);
                continue;
            }
            var dependencySucceeded = job.DependsOnJobId is not { } dependsOn || dependencies.GetValueOrDefault(dependsOn) == JobStatus.Succeeded;
            if (!job.CanBeClaimed(now, dependencySucceeded)) continue;

            job.Claim(name, now, O.Lease);
            if (await SaveOrSkipAsync(job, cancellationToken))
                claimed.Add(new ClaimedJob(job.Id, job.Step, job.ParametersJson, job.DryRun, job.Attempts));
        }

        if (runner == AutomationRunner.Agent && claimed.Count > 0
            && await db.AgentCheckIns.FirstOrDefaultAsync(a => a.Name == name, cancellationToken) is { } checkIn)
        {
            checkIn.LastJobAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        return claimed;
    }

    /// <summary>A runner reports how a job went; the checklist task follows.</summary>
    public async Task CompleteAsync(Guid jobId, string runnerName, JobResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        var name = CleanName(runnerName);
        var now = clock.GetUtcNow();
        var job = await db.AutomationJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Job not found.");
        var serviceCase = await LoadCaseAsync(job.CaseId, tracked: true, cancellationToken);
        var who = job.Runner == AutomationRunner.Giim ? "GIIM" : $"GIIM agent ({name})";

        if (result.Succeeded)
        {
            job.Succeed(name, Short(result.ResultJson, 4000), result.Log, now);
            if (job.DryRun)
                TryOnTask(serviceCase, job.TaskId, () => serviceCase.ReturnTaskToPerson(job.TaskId,
                    $"Dry run by {who}: {DryRunSummary(result.Log)}. Nothing was changed; do this by hand or run it for real.", now));
            else
            {
                // The account step reports the sign-in name and GUID; the mailbox step the email address.
                if (job.Step is AutomationStep.CreateAccount or AutomationStep.EnableRemoteMailbox)
                    await RecordAccountAsync(job.PersonId, result.ResultJson, cancellationToken);
                TryOnTask(serviceCase, job.TaskId, () => serviceCase.CompleteTask(job.TaskId, who, FirstLine(result.Log), now));
            }
        }
        else
        {
            var error = result.Error ?? "The step failed without saying why.";
            if (!job.Fail(name, error, result.Log, result.Retryable, now))
                TryOnTask(serviceCase, job.TaskId, () => serviceCase.FailAutomatedTask(job.TaskId, error, now));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>GIIM's own steps: waiting for the cloud account, and the welcome email. Called by the workers.</summary>
    public async Task<int> RunGiimStepsAsync(CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var claimed in await ClaimAsync(GiimRunner, AutomationRunner.Giim, 10, cancellationToken))
        {
            db.ChangeTracker.Clear();
            var job = await db.AutomationJobs.AsNoTracking().FirstAsync(j => j.Id == claimed.Id, cancellationToken);
            var person = await db.People.AsNoTracking().FirstAsync(p => p.Id == job.PersonId, cancellationToken);
            var result = job.Step switch
            {
                AutomationStep.WaitForCloudSync => await CloudSyncAsync(job, person, cancellationToken),
                AutomationStep.SendWelcomeEmail => await WelcomeEmailAsync(job, person, cancellationToken),
                _ => new JobResult(false, null, $"GIIM can't run {job.Step}.", null, false),
            };
            db.ChangeTracker.Clear();
            if (result is not null)
            {
                await CompleteAsync(job.Id, GiimRunner, result, cancellationToken);
                done++;
            }
        }
        return done;
    }

    // ---- GIIM's steps ----------------------------------------------------------------------------------------------------

    /// <summary>Done when the account is in Entra ID; otherwise looked at again shortly (null = postponed).</summary>
    private async Task<JobResult?> CloudSyncAsync(AutomationJob job, Person person, CancellationToken cancellationToken)
    {
        if (job.DryRun) return new JobResult(true, null, null, "Would wait until the new account appears in Entra ID", false);
        if (person.UserPrincipalName is not { } upn)
            return new JobResult(false, null, "GIIM doesn't know the new account's sign-in name (was it created by hand?). Tick this step off once it is in Entra ID.", null, false);

        var now = clock.GetUtcNow();
        if (await cloud.FindAsync(upn, cancellationToken) is { } account)
        {
            var tracked = await db.People.FirstAsync(p => p.Id == person.Id, cancellationToken);
            tracked.EntraObjectId = account.Id;
            await db.SaveChangesAsync(cancellationToken);
            return new JobResult(true, JsonSerializer.Serialize(account, JsonSerializerOptions.Web), null, $"{upn} is in Entra ID", false);
        }
        if (now - (job.StartedAt ?? now) > O.CloudSyncTimeout)
            return new JobResult(false, null, $"{upn} still isn't in Entra ID after {O.CloudSyncTimeout.TotalHours:0} hours. Check Entra Connect is syncing.", null, false);

        var tracking = await db.AutomationJobs.FirstAsync(j => j.Id == job.Id, cancellationToken);
        tracking.Postpone(GiimRunner, now + O.CloudSyncCheckEvery, $"{upn} isn't in Entra ID yet; checking again", now);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<JobResult> WelcomeEmailAsync(AutomationJob job, Person person, CancellationToken cancellationToken)
    {
        var manager = person.ManagerId is { } managerId
            ? await db.People.AsNoTracking().Where(p => p.Id == managerId)
                .Select(p => new { p.DisplayName, Address = p.Email ?? p.UserPrincipalName }).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (manager?.Address is null) return new JobResult(true, null, null, "No manager email on record; nobody to email", false);
        if (job.DryRun) return new JobResult(true, null, null, $"Would email {manager.Address} that {person.DisplayName} is set up", false);

        var link = giim.Value.BaseUrl is { } b ? $"{b}/?case={job.CaseId}" : null;
        db.Notifications.Add(ReminderEmails.Compose("WelcomeManager", manager.Address, manager.DisplayName,
            $"{person.DisplayName} is set up and ready to start",
            $"{person.DisplayName}'s account is enabled{(person.UserPrincipalName is { } upn ? $" ({upn})" : "")}. IT will help them sign in on their first morning.",
            [], "Open the starter checklist", link, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return new JobResult(true, null, null, $"Emailed {manager.Address}", false);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<PlannedStep>> PlanAsync(ServiceCase serviceCase, IReadOnlySet<Guid> active, DateTimeOffset now,
        CancellationToken cancellationToken, string createdBy = "preview")
    {
        var person = await db.People.AsNoTracking().FirstAsync(p => p.Id == serviceCase.PersonId, cancellationToken);
        var department = await db.Departments.AsNoTracking().Where(d => d.Id == person.DepartmentId)
            .Select(d => new { d.Name, d.Code }).FirstOrDefaultAsync(cancellationToken);
        var managerUpn = person.ManagerId is { } managerId
            ? await db.People.AsNoTracking().Where(p => p.Id == managerId).Select(p => p.UserPrincipalName).FirstOrDefaultAsync(cancellationToken)
            : null;
        var startDate = serviceCase.DueDate ?? person.StartDate
            ?? throw new DomainException("Set the start date first: the account is enabled on it.");

        var (given, surname) = PersonName.Split(person.DisplayName);
        var facts = new AccountFacts(person.EmployeeId, person.DisplayName, given, surname, department?.Name, department?.Code,
            person.JobTitle, person.Location, managerUpn, startDate, person.Track.ToString());

        var zone = TimeZoneInfo.FindSystemTimeZoneById(O.TimeZone);
        var local = startDate.ToDateTime(O.EnableAt);
        var enableAt = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return AutomationPlanner.Plan(serviceCase, facts, active, enableAt, now, O.DryRun, createdBy);
    }

    /// <summary>The agent reports the account it made; GIIM keeps the sign-in name and AD object GUID.</summary>
    private async Task RecordAccountAsync(Guid personId, string? resultJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return;
        CreatedAccount? account;
        try
        {
            account = JsonSerializer.Deserialize<CreatedAccount>(resultJson, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return;
        }
        if (account is null) return;
        var person = await db.People.FirstAsync(p => p.Id == personId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(account.UserPrincipalName)) person.UserPrincipalName = account.UserPrincipalName.Trim().ToLowerInvariant();
        if (account.ObjectGuid is { } guid) person.AdObjectGuid = guid;
        if (!string.IsNullOrWhiteSpace(account.Mail)) person.Email ??= account.Mail.Trim().ToLowerInvariant();
        person.UpdatedAt = clock.GetUtcNow();
    }

    private sealed record CreatedAccount(string? UserPrincipalName, Guid? ObjectGuid, string? Mail);

    private async Task<ServiceCase> LoadCaseAsync(Guid caseId, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.Cases.Include(c => c.Tasks).Where(c => c.Id == caseId);
        return await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Checklist not found.");
    }

    private async Task<IReadOnlyList<AgentView>> AgentsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        [.. (await db.AgentCheckIns.AsNoTracking().OrderByDescending(a => a.LastSeenAt).ToListAsync(cancellationToken))
            .Select(a => new AgentView(a.Name, a.LastSeenAt, now - a.LastSeenAt <= O.AgentOfflineAfter, a.Version, a.Directory, a.DryRun))];

    /// <summary>Saves a claim; if another runner took the job at the same moment, forgets it and moves on.</summary>
    private async Task<bool> SaveOrSkipAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(job).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Updates the task unless a person has already finished it by hand (the job is still recorded).</summary>
    private static void TryOnTask(ServiceCase serviceCase, Guid taskId, Action change)
    {
        if (serviceCase.Tasks.FirstOrDefault(t => t.Id == taskId) is not { Status: not (TaskState.Done or TaskState.Skipped) }) return;
        change();
    }

    private static string CleanName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("A runner must say who it is.") : Short(name.Trim(), 100)!;

    private static string? Short(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    /// <summary>"Dry run: would create x" → "would create x" (the note already says it was a dry run).</summary>
    private static string DryRunSummary(string? log)
    {
        var line = FirstLine(log) ?? "would succeed";
        return line.StartsWith("Dry run: ", StringComparison.OrdinalIgnoreCase) ? line["Dry run: ".Length..] : line;
    }

    private static string? FirstLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Short(text.Split('\n', 2)[0].Trim(), 500);
}
