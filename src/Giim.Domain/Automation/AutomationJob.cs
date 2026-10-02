using Giim.Domain.Common;

namespace Giim.Domain.Automation;

/// <summary>A checklist step that can be done automatically.</summary>
public enum AutomationStep
{
    /// <summary>On-prem agent: create the AD account (disabled, random password GIIM never sees).</summary>
    CreateAccount,
    /// <summary>GIIM: wait until Entra Connect has synced the new account to Entra ID.</summary>
    WaitForCloudSync,
    /// <summary>On-prem agent: Enable-RemoteMailbox (hybrid Exchange).</summary>
    EnableRemoteMailbox,
    /// <summary>On-prem agent: add the account to an AD group (security, licence or app access).</summary>
    AddToGroup,
    /// <summary>On-prem agent: enable the account, on the start date.</summary>
    EnableAccount,
    /// <summary>GIIM: email the manager that their starter is set up.</summary>
    SendWelcomeEmail,
    /// <summary>GIIM: add the account to a cloud-only Entra group, through Graph, once it has synced.</summary>
    AddToCloudGroup,
}

/// <summary>Who carries a step out.</summary>
public enum AutomationRunner { Agent, Giim }

public enum JobStatus { Queued, Running, Succeeded, Failed, Cancelled }

/// <summary>
/// One automation step for one checklist task. Jobs run in order (<see cref="DependsOnJobId"/>) and not before
/// <see cref="NotBefore"/>. A runner claims a job for a limited time (<see cref="LeaseUntil"/>): if it stops responding,
/// the claim lapses and the job is picked up again. Each job records what was done, so it can be audited.
/// </summary>
public sealed class AutomationJob : Entity
{
    public const int DefaultMaxAttempts = 3;
    public const int MaxLogLength = 8000;
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    public Guid CaseId { get; init; }
    public Guid TaskId { get; init; }
    public Guid PersonId { get; init; }
    public AutomationStep Step { get; init; }
    public AutomationRunner Runner { get; init; }

    /// <summary>What the runner needs, as JSON, fixed when the job is queued (names, groups, dates; never a password).</summary>
    public required string ParametersJson { get; init; }

    public Guid? DependsOnJobId { get; init; }
    public DateTimeOffset? NotBefore { get; private set; }

    /// <summary>Report what would be done without changing anything.</summary>
    public bool DryRun { get; init; }

    public required string CreatedBy { get; init; }
    public JobStatus Status { get; private set; } = JobStatus.Queued;
    public int Attempts { get; private set; }
    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    public string? ClaimedBy { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ResultJson { get; private set; }
    public string? Error { get; private set; }
    public string? Log { get; private set; }

    public static AutomationRunner RunnerFor(AutomationStep step) =>
        step is AutomationStep.WaitForCloudSync or AutomationStep.SendWelcomeEmail or AutomationStep.AddToCloudGroup
            ? AutomationRunner.Giim : AutomationRunner.Agent;

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;

    /// <summary>Whether a runner may take it now: queued and due, or running with a lapsed claim.</summary>
    public bool CanBeClaimed(DateTimeOffset now, bool dependencySucceeded) =>
        dependencySucceeded
        && (Status == JobStatus.Queued && (NotBefore is null || NotBefore <= now)
            || Status == JobStatus.Running && LeaseUntil < now);

    /// <summary>Not before this time, e.g. enabling the account on the start date.</summary>
    public void Delay(DateTimeOffset until)
    {
        if (Status != JobStatus.Queued) throw new DomainException("Only a queued step can be delayed.");
        NotBefore = until;
    }

    public void Claim(string runner, DateTimeOffset now, TimeSpan lease)
    {
        if (string.IsNullOrWhiteSpace(runner)) throw new DomainException("A runner must say who it is.");
        if (Status is not (JobStatus.Queued or JobStatus.Running)) throw new DomainException($"This job is {Status}.");
        Status = JobStatus.Running;
        ClaimedBy = runner.Trim();
        LeaseUntil = now + lease;
        StartedAt ??= now;
        Attempts++;
        UpdatedAt = now;
    }

    public void Succeed(string runner, string? resultJson, string? log, DateTimeOffset now)
    {
        EnsureHeldBy(runner);
        Status = JobStatus.Succeeded;
        ResultJson = resultJson;
        Error = null;
        Log = Trim(log);
        Finish(now);
    }

    /// <summary>
    /// Records a failure. A retryable one (e.g. the domain controller didn't answer) goes back in the queue with a
    /// growing delay until <see cref="MaxAttempts"/>; anything else stops so a person can look. Returns true if it will retry.
    /// </summary>
    public bool Fail(string runner, string error, string? log, bool retryable, DateTimeOffset now)
    {
        EnsureHeldBy(runner);
        Error = string.IsNullOrWhiteSpace(error) ? "The step failed without saying why." : Trim(error, 2000);
        Log = Trim(log);
        if (retryable && Attempts < MaxAttempts)
        {
            Status = JobStatus.Queued;
            NotBefore = now + Backoff[Math.Min(Attempts, Backoff.Length) - 1];
            ClaimedBy = null;
            LeaseUntil = null;
            UpdatedAt = now;
            return true;
        }
        Status = JobStatus.Failed;
        Finish(now);
        return false;
    }

    /// <summary>
    /// Not ready yet (e.g. the account hasn't synced to the cloud): hand the job back to be looked at again later. This
    /// isn't a failure, so it doesn't use up an attempt.
    /// </summary>
    public void Postpone(string runner, DateTimeOffset until, string note, DateTimeOffset now)
    {
        EnsureHeldBy(runner);
        Status = JobStatus.Queued;
        Attempts--;
        NotBefore = until;
        ClaimedBy = null;
        LeaseUntil = null;
        Log = Trim(note);
        UpdatedAt = now;
    }

    /// <summary>A technician tries a failed step again (after fixing the cause).</summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status != JobStatus.Failed) throw new DomainException("Only a failed step can be tried again.");
        Status = JobStatus.Queued;
        Attempts = 0;
        NotBefore = null;
        Error = null;
        CompletedAt = null;
        ClaimedBy = null;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    /// <summary>Stops a step that hasn't succeeded, e.g. to do it by hand instead.</summary>
    public void Cancel(string actor, DateTimeOffset now)
    {
        if (Status is JobStatus.Succeeded or JobStatus.Cancelled) throw new DomainException($"This step is already {Status.ToString().ToLowerInvariant()}.");
        Status = JobStatus.Cancelled;
        Error = $"Stopped by {actor}";
        Finish(now);
    }

    private void EnsureHeldBy(string runner)
    {
        if (Status != JobStatus.Running || !string.Equals(ClaimedBy, runner?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("This job isn't held by that runner (its claim may have lapsed and been given to another).");
    }

    private void Finish(DateTimeOffset now)
    {
        CompletedAt = now;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    private static string? Trim(string? text, int max = MaxLogLength) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : text[..max];
}
