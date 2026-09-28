namespace Giim.Connectors.Okta;

/// <summary>Okta Management API. Read-only in Phase 1; group changes arrive in Phase 3.</summary>
public interface IOktaClient
{
    Task<OktaUser?> GetUserByLoginAsync(string login, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetUserGroupNamesAsync(string oktaUserId, CancellationToken cancellationToken = default);
}

public sealed record OktaUser(string Id, string Login, string Status, string? Department);
