namespace Giim.Domain.Notifications;

/// <summary>
/// The last local day a scheduled job (the IT digest, the warranty list) ran, so it runs once per day or week however
/// often the workers check, and survives restarts.
/// </summary>
public sealed class ScheduledJobRun
{
    public required string Name { get; init; }
    public DateOnly LastRunOn { get; set; }
    public DateTimeOffset LastRunAt { get; set; }
    public string? LastResult { get; set; }
}
