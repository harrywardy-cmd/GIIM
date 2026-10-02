using Giim.Infrastructure.Automation;

namespace Giim.Workers;

/// <summary>
/// Runs GIIM's own automation steps every 30 seconds: waiting for a new account to reach Entra ID, and the welcome
/// email on the start date. (The on-prem agent runs the AD and Exchange steps.)
/// </summary>
internal sealed partial class AutomationWorker(IServiceScopeFactory scopes, ILogger<AutomationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var done = await scope.ServiceProvider.GetRequiredService<AutomationService>().RunGiimStepsAsync(stoppingToken);
                if (done > 0) LogDone(logger, done);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed round must not stop the worker; the next round tries again.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Automation: {Count} GIIM step(s) finished.")]
    private static partial void LogDone(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Automation round failed; will try again.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
