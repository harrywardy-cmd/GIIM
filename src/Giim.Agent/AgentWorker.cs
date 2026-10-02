using System.Reflection;
using Giim.Agent.Accounts;
using Microsoft.Extensions.Options;

namespace Giim.Agent;

/// <summary>
/// The agent's loop: check in, take any jobs, carry them out one at a time, report each result, wait, repeat. If GIIM
/// can't be reached it waits longer each time (up to five minutes) and carries on; a job it couldn't report is
/// handed out again by GIIM once its claim lapses, and every step is safe to repeat.
/// </summary>
internal sealed partial class AgentWorker(GiimClient giim, JobRunner runner, IDirectory directory, IOptions<AgentOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    /// <summary>"1.0.0 (6d48af8)": the version, and the start of the commit it was built from.</summary>
    private static readonly string Version = ShortVersion(
        typeof(AgentWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string ShortVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "dev";
        var parts = informational.Split('+', 2);
        return parts.Length == 2 && parts[1].Length >= 7 ? $"{parts[0]} ({parts[1][..7]})" : parts[0];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarting(logger, options.Value.EffectiveName, directory.Kind, options.Value.GiimUrl, options.Value.DryRun);
        var wait = TimeSpan.FromSeconds(15);
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var reply = await giim.CheckInAsync(directory.Kind, Version, stoppingToken);
                if (failures > 0) LogReconnected(logger);
                failures = 0;
                wait = TimeSpan.FromSeconds(Math.Clamp(reply.PollSeconds, 5, 300));

                foreach (var job in await giim.ClaimAsync(stoppingToken))
                {
                    LogJob(logger, job.Step, job.Id, job.DryRun || options.Value.DryRun);
                    var outcome = await runner.RunAsync(job, stoppingToken);
                    if (outcome.Succeeded) LogDone(logger, job.Step, outcome.Log);
                    else LogFailed(logger, job.Step, outcome.Error, outcome.Retryable);
                    await giim.ReportAsync(job.Id, outcome, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException e)
            {
                failures++;
                wait = TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(failures, 5))));
                LogUnreachable(logger, e.Message, wait.TotalSeconds);
            }
            await Task.Delay(wait, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "GIIM agent {Name} starting: directory {Directory}, GIIM at {Url}, agent dry run {DryRun}.")]
    private static partial void LogStarting(ILogger logger, string name, string directory, Uri url, bool dryRun);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Step} ({JobId}), dry run: {DryRun}")]
    private static partial void LogJob(ILogger logger, string step, Guid jobId, bool dryRun);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Step} done: {Log}")]
    private static partial void LogDone(ILogger logger, string step, string? log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Step} failed: {Error} (will retry: {Retryable})")]
    private static partial void LogFailed(ILogger logger, string step, string? error, bool retryable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't reach GIIM ({Error}); trying again in {Seconds} s.")]
    private static partial void LogUnreachable(ILogger logger, string error, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to GIIM again.")]
    private static partial void LogReconnected(ILogger logger);
}
