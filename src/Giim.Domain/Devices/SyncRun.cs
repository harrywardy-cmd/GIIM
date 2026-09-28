using Giim.Domain.Common;

namespace Giim.Domain.Devices;

public enum SyncRunStatus { Running, Succeeded, Failed }

/// <summary>History of each sync with an external system, so the UI can say how fresh the data is.</summary>
public sealed class SyncRun : Entity
{
    public required string Source { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;

    public int DevicesSeen { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public string? Error { get; set; }
}
