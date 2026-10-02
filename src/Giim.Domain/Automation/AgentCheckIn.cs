namespace Giim.Domain.Automation;

/// <summary>When an on-prem agent last checked in, so GIIM can tell whether automation is running.</summary>
public sealed class AgentCheckIn
{
    /// <summary>The agent's name (normally its server's name).</summary>
    public required string Name { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? Version { get; set; }
    public string? Directory { get; set; }
    public bool DryRun { get; set; }
    public DateTimeOffset? LastJobAt { get; set; }
}
