namespace Giim.Domain.Requests;

public enum RequestEventType
{
    Submitted,
    Approved,
    Rejected,
    InfoRequested,
    InfoProvided,
    Cancelled,
    Ordered,
    Received,
    Completed,
    Commented,
}

/// <summary>
/// One entry in a request's permanent history: who did what, when, and why. Append-only, like the asset timeline:
/// the app refuses to change or delete these, and so does the database.
/// </summary>
public sealed class DeviceRequestEvent
{
    public long Id { get; init; }
    public Guid RequestId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public RequestEventType Type { get; init; }
    public RequestStatus FromStatus { get; init; }
    public RequestStatus ToStatus { get; init; }
    public required string Actor { get; init; }
    public required string ActorName { get; init; }
    public required string Summary { get; init; }
    public string? Comment { get; init; }
}
