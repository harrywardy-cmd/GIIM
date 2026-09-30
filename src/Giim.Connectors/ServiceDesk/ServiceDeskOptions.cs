namespace Giim.Connectors.ServiceDesk;

public enum ServiceDeskMode
{
    /// <summary>Not connected. The webhook is switched off and nothing is sent.</summary>
    None,

    /// <summary>A stand-in: tickets come from a sample file and notes are written to a log. For development.</summary>
    File,

    /// <summary>ServiceDesk Plus Cloud through its REST API (v3), signed in through Zoho.</summary>
    Api,
}

/// <summary>
/// ServiceDesk Plus Cloud (AU data centre). The client secret, refresh token and webhook secret come from user-secrets
/// locally or Key Vault in Azure, never appsettings. See docs/servicedesk-setup.md.
/// </summary>
public sealed class ServiceDeskOptions
{
    public const string SectionName = "ServiceDesk";

    public ServiceDeskMode Mode { get; set; } = ServiceDeskMode.None;

    /// <summary>The ServiceDesk Plus site, e.g. https://servicedeskplus.net.au/ (AU data centre).</summary>
    public Uri SiteUrl { get; set; } = new("https://servicedeskplus.net.au/");

    /// <summary>The portal name in ticket addresses (…/app/itdesk/…).</summary>
    public string Portal { get; set; } = "itdesk";

    /// <summary>Zoho accounts server for the data centre: accounts.zoho.com.au for AU.</summary>
    public Uri ZohoAccountsUrl { get; set; } = new("https://accounts.zoho.com.au/");

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";

    /// <summary>Shared secret ServiceDesk Plus sends in the X-GIIM-Webhook-Secret header. Empty turns the webhook off.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>File mode: sample tickets (in the API's JSON shape) and where notes are written.</summary>
    public string SampleFilePath { get; set; } = "samples/servicedesk-requests.json";
    public string NotesFilePath { get; set; } = "artifacts/servicedesk/notes.log";

    /// <summary>Dates in tickets are read in this time zone.</summary>
    public string TimeZone { get; set; } = "Australia/Sydney";

    /// <summary>Raise device requests for a starter's hardware straight away (their manager approves as usual).</summary>
    public bool AutoRaiseDeviceRequests { get; set; } = true;

    /// <summary>Resolve the ticket when its checklist is complete. Agree this with the service desk team first.</summary>
    public bool ResolveWhenComplete { get; set; }

    public TicketMapping Starter { get; set; } = new();
    public TicketMapping Leaver { get; set; } = new();
}

/// <summary>Which request templates mean a starter (or leaver), and which ticket fields hold each detail.</summary>
public sealed class TicketMapping
{
    /// <summary>Request template names, e.g. "New Starter". Matched ignoring case.</summary>
    public List<string> Templates { get; set; } = [];

    /// <summary>
    /// GIIM detail → ServiceDesk Plus field key (e.g. "EmployeeId" → "udf_sline_301"). Starters: EmployeeId, Name,
    /// Department (code or name), JobTitle, ManagerEmail, StartDate, Track (Full/Light). Leavers: EmployeeId, Email, LastDay.
    /// </summary>
    public Dictionary<string, string> Fields { get; set; } = [];
}
