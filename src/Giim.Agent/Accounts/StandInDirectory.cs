using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Giim.Agent.Accounts;

/// <summary>
/// A pretend Active Directory in a JSON file, for a developer PC and demonstrations. It behaves like AD where it matters:
/// accounts are found by employee ID, sign-in names must be unique, steps are safe to repeat. GIIM's stand-in cloud
/// lookup reads the same file to pretend Entra Connect has synced an account (cloudId).
/// Groups whose name starts with "MISSING-" don't exist, to try out a failing step.
/// </summary>
internal sealed class StandInDirectory(IOptions<AgentOptions> options, TimeProvider clock) : IDirectory, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = RepoPath(options.Value.StandInPath);

    public string Kind => "Stand-in";

    public void Dispose() => _lock.Dispose();

    public async Task<DirectoryUser?> FindByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Users.FirstOrDefault(u => string.Equals(u.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))?.ToUser();

    public async Task<bool> SamAccountNameTakenAsync(string samAccountName, CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Users.Any(u => string.Equals(u.SamAccountName, samAccountName, StringComparison.OrdinalIgnoreCase));

    public Task<DirectoryUser> CreateUserAsync(NewDirectoryUser user, CancellationToken cancellationToken) =>
        ChangeAsync(file =>
        {
            if (file.Users.FirstOrDefault(u => string.Equals(u.EmployeeId, user.EmployeeId, StringComparison.OrdinalIgnoreCase)) is { } existing)
                return existing;
            if (file.Users.Any(u => string.Equals(u.SamAccountName, user.SamAccountName, StringComparison.OrdinalIgnoreCase)))
                throw new DirectoryException($"The sign-in name {user.SamAccountName} is already taken.", retryable: false);

            var created = new StandInUser
            {
                EmployeeId = user.EmployeeId,
                SamAccountName = user.SamAccountName,
                UserPrincipalName = user.UserPrincipalName,
                ObjectGuid = Guid.NewGuid(),
                CloudId = Guid.NewGuid(),
                DistinguishedName = $"CN={user.DisplayName},{user.OrganizationalUnit}",
                DisplayName = user.DisplayName,
                GivenName = user.GivenName,
                Surname = user.Surname,
                Department = user.Department,
                Title = user.JobTitle,
                Office = user.Office,
                Manager = user.ManagerUserPrincipalName,
                Enabled = false,
                CreatedAt = clock.GetUtcNow(),
            };
            file.Users.Add(created);
            return created;
        }, cancellationToken);

    public Task<DirectoryUser> EnableRemoteMailboxAsync(DirectoryUser user, string remoteRoutingAddress, CancellationToken cancellationToken) =>
        ChangeAsync(file =>
        {
            var u = Find(file, user);
            u.RemoteMailbox = true;
            u.RemoteRoutingAddress = remoteRoutingAddress;
            u.Mail = u.UserPrincipalName;
            return u;
        }, cancellationToken);

    public Task<DirectoryUser> AddToGroupAsync(DirectoryUser user, string group, CancellationToken cancellationToken) =>
        ChangeAsync(file =>
        {
            if (group.StartsWith("MISSING-", StringComparison.OrdinalIgnoreCase))
                throw new DirectoryException($"There is no AD group called {group}.", retryable: false);
            var u = Find(file, user);
            if (!u.MemberOf.Contains(group, StringComparer.OrdinalIgnoreCase)) u.MemberOf.Add(group);
            return u;
        }, cancellationToken);

    public Task<DirectoryUser> EnableAsync(DirectoryUser user, CancellationToken cancellationToken) =>
        ChangeAsync(file =>
        {
            var u = Find(file, user);
            u.Enabled = true;
            return u;
        }, cancellationToken);

    private static StandInUser Find(StandInFile file, DirectoryUser user) =>
        file.Users.FirstOrDefault(u => u.ObjectGuid == user.ObjectGuid)
        ?? throw new DirectoryException($"{user.SamAccountName} is no longer in the directory.", retryable: false);

    private async Task<DirectoryUser> ChangeAsync(Func<StandInFile, StandInUser> change, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadUnlockedAsync(cancellationToken);
            var user = change(file);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write to a new file and swap it in, so a reader (GIIM's cloud lookup) never sees half a file.
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(file, Json), cancellationToken);
            File.Move(temp, _path, overwrite: true);
            return user.ToUser();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StandInFile> ReadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StandInFile> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new StandInFile();
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<StandInFile>(stream, Json, cancellationToken) ?? new StandInFile();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static string RepoPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx")))
                return Path.Combine(dir.FullName, path);
        return Path.Combine(AppContext.BaseDirectory, path);
    }

    private sealed class StandInFile
    {
        public List<StandInUser> Users { get; init; } = [];
    }

    private sealed class StandInUser
    {
        public required string EmployeeId { get; init; }
        public required string SamAccountName { get; init; }
        public required string UserPrincipalName { get; init; }
        public Guid ObjectGuid { get; init; }
        /// <summary>The Entra object ID it will have once "synced" (GIIM's stand-in cloud lookup reads it).</summary>
        public Guid CloudId { get; init; }
        public required string DistinguishedName { get; init; }
        public string? DisplayName { get; init; }
        public string? GivenName { get; init; }
        public string? Surname { get; init; }
        public string? Department { get; init; }
        public string? Title { get; init; }
        public string? Office { get; init; }
        public string? Manager { get; init; }
        public bool Enabled { get; set; }
        public bool RemoteMailbox { get; set; }
        public string? RemoteRoutingAddress { get; set; }
        public string? Mail { get; set; }
        public List<string> MemberOf { get; init; } = [];
        public DateTimeOffset CreatedAt { get; init; }

        public DirectoryUser ToUser() => new(EmployeeId, SamAccountName, UserPrincipalName, ObjectGuid, DistinguishedName, Enabled,
            RemoteMailbox, Mail, [.. MemberOf]);
    }
}
