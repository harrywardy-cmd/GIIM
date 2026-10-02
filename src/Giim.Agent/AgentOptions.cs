namespace Giim.Agent;

internal enum AgentAuth
{
    /// <summary>A shared key (X-GIIM-Agent-Key). For a developer PC.</summary>
    Key,

    /// <summary>The agent's own Entra app registration, signing in with a certificate from this server's store.</summary>
    Entra,
}

internal enum DirectoryKind
{
    /// <summary>A pretend AD in a JSON file, so the whole flow runs on a developer PC.</summary>
    StandIn,

    /// <summary>The real Active Directory and Exchange Management Shell. Not built yet: see docs/onprem-agent.md.</summary>
    ActiveDirectory,
}

internal sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>GIIM's address, e.g. https://giim.company.com.au.</summary>
    public Uri GiimUrl { get; set; } = new("http://localhost:5080");

    /// <summary>How this agent is named in GIIM; defaults to the server's name.</summary>
    public string? Name { get; set; }

    public AgentAuth Auth { get; set; } = AgentAuth.Key;
    public string? Key { get; set; }

    /// <summary>Entra: this agent's tenant and app registration, and the thumbprint of its certificate (LocalMachine\My).</summary>
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? CertificateThumbprint { get; set; }

    /// <summary>Entra: GIIM's own app registration (client ID); the agent asks for a token for it.</summary>
    public string? GiimClientId { get; set; }

    public DirectoryKind Directory { get; set; } = DirectoryKind.StandIn;

    /// <summary>StandIn: the pretend directory file, relative to the repository root unless rooted.</summary>
    public string StandInPath { get; set; } = "artifacts/agent/directory.json";

    public int MaxJobsPerPoll { get; set; } = 5;

    /// <summary>Belt and braces: when true this agent never changes anything, whatever GIIM asks.</summary>
    public bool DryRun { get; set; }

    public string EffectiveName => string.IsNullOrWhiteSpace(Name) ? Environment.MachineName : Name.Trim();
}

/// <summary>
/// How new accounts are named and where they go. These are the AD administrators' decisions, kept here on their
/// server rather than in GIIM.
/// </summary>
internal sealed class AccountRules
{
    public const string SectionName = "Accounts";

    /// <summary>Sign-in name: {first}, {last}, {f} (first initial) and {l}, e.g. "{first}.{last}" → priya.patel.</summary>
    public string NameFormat { get; set; } = "{first}.{last}";

    /// <summary>The UPN and email suffix, e.g. contoso.com.au.</summary>
    public string UpnSuffix { get; set; } = "contoso.example";

    /// <summary>OU for a department code (from GIIM); departments not listed go to <see cref="DefaultOu"/>.</summary>
    public Dictionary<string, string> DepartmentOus { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultOu { get; set; } = "OU=New Starters,OU=Staff,DC=contoso,DC=example";

    /// <summary>Hybrid Exchange: the tenant's routing domain for Enable-RemoteMailbox, e.g. contoso.mail.onmicrosoft.com.</summary>
    public string RemoteRoutingDomain { get; set; } = "contoso.mail.onmicrosoft.com";

    /// <summary>
    /// If set, the only groups the agent will add people to: names or prefixes ending in *, e.g. "APP-*", "LIC-*".
    /// Recommended, so GIIM can never ask for anything else. Privileged groups are refused whatever this says.
    /// </summary>
    public List<string> AllowedGroups { get; set; } = [];

    /// <summary>Built-in AD groups that grant control of the domain or servers. Never added by the agent.</summary>
    public static readonly IReadOnlySet<string> Privileged = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators", "Account Operators", "Server Operators",
        "Backup Operators", "Print Operators", "DnsAdmins", "Group Policy Creator Owners", "Enterprise Key Admins",
        "Key Admins", "Organization Management", "Exchange Trusted Subsystem", "Exchange Windows Permissions",
    };

    /// <summary>Why the agent won't add someone to this group, or null if it may.</summary>
    public string? GroupRefusal(string group)
    {
        if (Privileged.Contains(group.Trim())) return $"{group} is a privileged group; the agent never adds anyone to it.";
        if (AllowedGroups.Count == 0) return null;
        var allowed = AllowedGroups.Any(rule => rule.EndsWith('*')
            ? group.StartsWith(rule[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(group, rule, StringComparison.OrdinalIgnoreCase));
        return allowed ? null : $"{group} isn't on this agent's list of groups it may add people to (Accounts:AllowedGroups).";
    }

    public string OuFor(string? departmentCode) =>
        departmentCode is not null && DepartmentOus.TryGetValue(departmentCode, out var ou) ? ou : DefaultOu;
}
