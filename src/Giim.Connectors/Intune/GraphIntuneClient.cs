using Azure.Core;
using Giim.Connectors.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.Intune;

/// <summary>
/// Reads managed devices from Microsoft Graph with app-only auth. Needs the application permission
/// DeviceManagementManagedDevices.Read.All (read-only); see docs/intune-app-registration.md.
/// </summary>
public sealed class GraphIntuneClient(
    HttpClient http, TokenCredential credential, IOptions<IntuneOptions> options, ILogger<GraphIntuneClient> logger) : IIntuneClient
{
    private const string Fields =
        "id,deviceName,serialNumber,manufacturer,model,operatingSystem,userPrincipalName,complianceState,lastSyncDateTime,enrolledDateTime";

    public IAsyncEnumerable<IntuneDevice> GetManagedDevicesAsync(CancellationToken cancellationToken = default) =>
        GraphReader.ReadAllAsync<IntuneDevice>(http, credential,
            new Uri(options.Value.GraphBaseUrl, $"deviceManagement/managedDevices?$select={Fields}"),
            options.Value.MaxRetries, logger, cancellationToken);
}
