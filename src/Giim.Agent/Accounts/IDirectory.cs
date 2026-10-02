namespace Giim.Agent.Accounts;

/// <summary>An AD user account as the agent sees it.</summary>
internal sealed record DirectoryUser(
    string EmployeeId,
    string SamAccountName,
    string UserPrincipalName,
    Guid ObjectGuid,
    string DistinguishedName,
    bool Enabled,
    bool RemoteMailbox,
    string? Mail,
    IReadOnlyList<string> MemberOf);

/// <summary>What the agent asks the directory to create. There is no password here: the directory sets a random one.</summary>
internal sealed record NewDirectoryUser(
    string EmployeeId,
    string SamAccountName,
    string UserPrincipalName,
    string DisplayName,
    string GivenName,
    string Surname,
    string? Department,
    string? JobTitle,
    string? Office,
    string? ManagerUserPrincipalName,
    string OrganizationalUnit);

/// <summary>A directory refused or couldn't do something. Retryable: worth trying again later (e.g. a DC was busy).</summary>
internal sealed class DirectoryException(string message, bool retryable) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// The AD and Exchange operations the agent needs. The stand-in keeps a JSON file; the real implementation will use
/// the ActiveDirectory module and the Exchange Management Shell (docs/onprem-agent.md). Every operation is safe to repeat.
/// </summary>
internal interface IDirectory
{
    /// <summary>A short name for GIIM's Setup page, e.g. "Stand-in".</summary>
    string Kind { get; }

    Task<DirectoryUser?> FindByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken);
    Task<bool> SamAccountNameTakenAsync(string samAccountName, CancellationToken cancellationToken);

    /// <summary>Creates the account disabled, with a random password nobody is told.</summary>
    Task<DirectoryUser> CreateUserAsync(NewDirectoryUser user, CancellationToken cancellationToken);

    Task<DirectoryUser> EnableRemoteMailboxAsync(DirectoryUser user, string remoteRoutingAddress, CancellationToken cancellationToken);
    Task<DirectoryUser> AddToGroupAsync(DirectoryUser user, string group, CancellationToken cancellationToken);
    Task<DirectoryUser> EnableAsync(DirectoryUser user, CancellationToken cancellationToken);
}
