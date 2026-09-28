namespace Giim.Domain.Auditing;

/// <summary>Append-only record of every change and every call made to an external system.</summary>
public sealed class AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
    public string? CorrelationId { get; init; }
}
