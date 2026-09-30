namespace Giim.Connectors.ServiceDesk;

/// <summary>ServiceDesk Plus Cloud (AU data centre), REST API v3: read a ticket, add a note, resolve it.</summary>
public interface IServiceDeskClient
{
    /// <summary>The ticket with this internal ID (the long number the API uses), or null if there isn't one.</summary>
    Task<ServiceDeskRequest?> GetRequestAsync(string requestKey, CancellationToken cancellationToken = default);

    /// <summary>The internal ID for the ticket number people see (display ID), or null if there isn't one.</summary>
    Task<string?> FindRequestKeyAsync(string displayId, CancellationToken cancellationToken = default);

    /// <summary>Adds a note visible to technicians only (not the requester).</summary>
    Task AddNoteAsync(string requestKey, string html, CancellationToken cancellationToken = default);

    Task ResolveAsync(string requestKey, string resolution, CancellationToken cancellationToken = default);
}

/// <summary>
/// A ticket. <see cref="Fields"/> holds every field by its key (standard fields and the udf_… fields the SDP
/// administrator added), as text; dates are the milliseconds-since-1970 value SDP stores.
/// </summary>
public sealed record ServiceDeskRequest(
    string Key,
    string DisplayId,
    string Subject,
    string? Template,
    string? Status,
    string? RequesterEmail,
    IReadOnlyDictionary<string, string?> Fields);
