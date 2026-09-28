namespace Giim.Connectors.ServiceDesk;

/// <summary>Secrets (client secret, refresh token) come from Azure Key Vault, never appsettings.</summary>
public sealed class ServiceDeskOptions
{
    public const string SectionName = "ServiceDesk";

    public Uri ApiBaseUrl { get; set; } = new("https://servicedeskplus.net.au/api/v3/");
    public Uri ZohoAccountsUrl { get; set; } = new("https://accounts.zoho.com.au/");
    public string Portal { get; set; } = "itdesk";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}
