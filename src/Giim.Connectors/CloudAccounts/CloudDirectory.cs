using System.Text.Json;
using Azure.Core;
using Giim.Connectors.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.CloudAccounts;

/// <summary>An account as Entra ID sees it, once Entra Connect has synced it from AD.</summary>
public sealed record CloudAccount(Guid Id, string UserPrincipalName);

/// <summary>Finds a newly created account in Entra ID. Read-only; used to tell when Entra Connect has synced it.</summary>
public interface ICloudDirectory
{
    Task<CloudAccount?> FindAsync(string userPrincipalName, CancellationToken cancellationToken);
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

    private sealed record GraphUser(string Id, string? UserPrincipalName);
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

    internal static string RepoPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Giim.slnx")))
                return Path.Combine(dir.FullName, path);
        return Path.Combine(AppContext.BaseDirectory, path);
    }
}
