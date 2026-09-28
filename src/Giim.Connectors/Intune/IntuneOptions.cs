namespace Giim.Connectors.Intune;

public enum IntuneSource
{
    /// <summary>Reads a Graph-shaped JSON export from disk. For development and tests.</summary>
    File,

    /// <summary>Reads live from Microsoft Graph.</summary>
    Graph,
}

public sealed class IntuneOptions
{
    public const string SectionName = "Intune";

    public IntuneSource Source { get; set; } = IntuneSource.File;

    /// <summary>Used when Source is File.</summary>
    public string FilePath { get; set; } = "samples/intune-devices.json";

    /// <summary>
    /// Graph app registration. When all three are set a client secret is used (local testing);
    /// otherwise DefaultAzureCredential, i.e. the App Service managed identity in Azure. Secrets come
    /// from user-secrets or Key Vault, never appsettings.
    /// </summary>
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");

    /// <summary>How many times a throttled (429) or unavailable (503/504) request is retried.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>How often the background worker syncs. Zero disables the schedule.</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(4);
}
