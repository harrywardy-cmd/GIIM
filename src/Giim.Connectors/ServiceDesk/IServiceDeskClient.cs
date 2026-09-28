namespace Giim.Connectors.ServiceDesk;

/// <summary>ServiceDesk Plus Cloud (AU data centre), REST API v3.</summary>
public interface IServiceDeskClient
{
    Task<ServiceDeskRequest?> GetRequestAsync(string requestId, CancellationToken cancellationToken = default);

    Task AddNoteAsync(string requestId, string note, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ServiceDeskAsset> GetAssetsAsync(CancellationToken cancellationToken = default);
}

public sealed record ServiceDeskRequest(
    string Id,
    string Subject,
    string Template,
    string Status,
    string? RequesterEmail,
    IReadOnlyDictionary<string, string?> Fields);

public sealed record ServiceDeskAsset(
    string Id,
    string Name,
    string? SerialNumber,
    string? AssetTag,
    string? Product,
    string? State,
    string? AssignedUser);
