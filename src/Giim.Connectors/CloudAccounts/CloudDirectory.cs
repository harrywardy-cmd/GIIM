using System.Text.Json;
using Azure.Core;
using Giim.Connectors.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.CloudAccounts;

/// <summary>An account as Entra ID sees it, once Entra Connect has synced it from AD.</summary>
public sealed record CloudAccount(Guid Id, string UserPrincipalName);

/// <summary>Graph refused or couldn't do something. Retryable: worth trying again later.</summary>
public sealed class CloudDirectoryException(string message, bool retryable) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// Entra ID for automation: finds a newly created account (to tell when Entra Connect has synced it), and adds accounts to
/// cloud-only groups. Groups synced from AD, groups that grant admin roles and dynamic groups are refused.
/// </summary>
public interface ICloudDirectory
{
    Task<CloudAccount?> FindAsync(string userPrincipalName, CancellationToken cancellationToken);

    /// <summary>Adds the account to the cloud-only group; false if it was already a member.</summary>
    Task<bool> AddToGroupAsync(Guid accountId, string groupName, CancellationToken cancellationToken);
}

public enum CloudDirectorySource
{
    /// <summary>The stand-in agent's pretend directory (artifacts/agent/directory.json), "synced" after a delay.</summary>
    File,

    /// <summary>Entra ID through Microsoft Graph (User.Read.All, already granted for the staff directory).</summary>
    Graph,
}

public sealed class CloudDirectoryOptions
{
    public const string SectionName = "Automation:CloudDirectory";

    public CloudDirectorySource Source { get; set; } = CloudDirectorySource.File;
    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");
    public int MaxRetries { get; set; } = 5;

    /// <summary>File: the stand-in agent's directory file, relative to the repository root unless rooted.</summary>
    public string FilePath { get; set; } = "artifacts/agent/directory.json";

    /// <summary>File: how long the stand-in pretends Entra Connect takes (it runs about every 30 minutes for real).</summary>
    public TimeSpan FileSyncDelay { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class GraphCloudDirectory(HttpClient http, TokenCredential credential, IOptions<CloudDirectoryOptions> options,
    ILogger<GraphCloudDirectory> logger) : ICloudDirectory
{
    public async Task<CloudAccount?> FindAsync(string userPrincipalName, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var user = await GraphReader.GetAsync<GraphUser>(http, credential,
            new Uri(o.GraphBaseUrl, $"users/{Uri.EscapeDataString(userPrincipalName)}?$select=id,userPrincipalName"),
            o.MaxRetries, logger, cancellationToken);
        return user is null || !Guid.TryParse(user.Id, out var id) ? null : new CloudAccount(id, user.UserPrincipalName ?? userPrincipalName);
    }

    public async Task<bool> AddToGroupAsync(Guid accountId, string groupName, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var filter = Uri.EscapeDataString($"displayName eq '{groupName.Replace("'", "''", StringComparison.Ordinal)}'");
        var found = await GraphReader.GetAsync<GroupPage>(http, credential,
            new Uri(o.GraphBaseUrl, $"groups?$filter={filter}&$select=id,displayName,onPremisesSyncEnabled,groupTypes,isAssignableToRole"),
            o.MaxRetries, logger, cancellationToken);
        var group = (found?.Value.Count ?? 0) switch
        {
            0 => throw new CloudDirectoryException($"There is no Entra group called {groupName}.", retryable: false),
            1 => found!.Value[0],
            _ => throw new CloudDirectoryException($"More than one Entra group is called {groupName}; rename one.", retryable: false),
        };
        if (group.OnPremisesSyncEnabled == true)
            throw new CloudDirectoryException($"{groupName} is synced from AD, so it can only be changed there: untick 'cloud-only' on the profile item and the agent will add it.", retryable: false);
        if (group.IsAssignableToRole == true)
            throw new CloudDirectoryException($"{groupName} grants admin roles; GIIM never adds people to it.", retryable: false);
        if (group.GroupTypes?.Contains("DynamicMembership", StringComparer.OrdinalIgnoreCase) == true)
            throw new CloudDirectoryException($"{groupName} has dynamic membership; Entra adds people to it by its rule.", retryable: false);

        using var response = await GraphReader.PostAsync(http, credential, new Uri(o.GraphBaseUrl, $"groups/{group.Id}/members/$ref"),
            $$"""{"@odata.id":"{{o.GraphBaseUrl}}directoryObjects/{{accountId}}"}""", o.MaxRetries, logger, cancellationToken);
        if (response.IsSuccessStatusCode) return true;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && body.Contains("already exist", StringComparison.OrdinalIgnoreCase)) return false;
        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.Forbidden => new CloudDirectoryException(
                $"GIIM isn't allowed to change {groupName}: add the group to GIIM's administrative unit (docs/onprem-agent.md).", retryable: false),
            System.Net.HttpStatusCode.NotFound => new CloudDirectoryException("The account isn't in Entra ID yet.", retryable: true),
            _ => new CloudDirectoryException($"Graph returned {(int)response.StatusCode} adding to {groupName}.", retryable: (int)response.StatusCode >= 500),
        };
    }

    private sealed record GraphUser(string Id, string? UserPrincipalName);
    private sealed record GroupPage(IReadOnlyList<GraphGroup> Value);
    private sealed record GraphGroup(string Id, string? DisplayName, bool? OnPremisesSyncEnabled, IReadOnlyList<string>? GroupTypes, bool? IsAssignableToRole);
}

/// <summary>
/// Stand-in for a developer PC: reads the stand-in agent's pretend AD and treats an account as synced to the cloud once
/// it has existed for <see cref="CloudDirectoryOptions.FileSyncDelay"/>, like Entra Connect.
/// </summary>
public sealed class FileCloudDirectory(IOptions<CloudDirectoryOptions> options, TimeProvider clock) : ICloudDirectory
{
    public async Task<CloudAccount?> FindAsync(string userPrincipalName, CancellationToken cancellationToken)
    {
        var path = RepoPath(options.Value.FilePath);
        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("users", out var users)) return null;

        foreach (var user in users.EnumerateArray())
        {
            if (!string.Equals(user.GetProperty("userPrincipalName").GetString(), userPrincipalName, StringComparison.OrdinalIgnoreCase)) continue;
            var created = user.GetProperty("createdAt").GetDateTimeOffset();
            return clock.GetUtcNow() - created < options.Value.FileSyncDelay
                ? null
                : new CloudAccount(user.GetProperty("cloudId").GetGuid(), user.GetProperty("userPrincipalName").GetString()!);
        }
        return null;
    }

    /// <summary>Stand-in: memberships are kept in cloud-groups.json next to the pretend AD. "MISSING-" groups don't exist.</summary>
    public async Task<bool> AddToGroupAsync(Guid accountId, string groupName, CancellationToken cancellationToken)
    {
        if (groupName.StartsWith("MISSING-", StringComparison.OrdinalIgnoreCase))
            throw new CloudDirectoryException($"There is no Entra group called {groupName}.", retryable: false);
        var path = Path.Combine(Path.GetDirectoryName(RepoPath(options.Value.FilePath))!, "cloud-groups.json");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var groups = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? []
                : [];
            var members = groups.TryGetValue(groupName, out var list) ? list : groups[groupName] = [];
            if (members.Contains(accountId)) return false;
            members.Add(accountId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(groups, Indented), cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static string RepoPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx")))
                return Path.Combine(dir.FullName, path);
        return Path.Combine(AppContext.BaseDirectory, path);
    }
}
