using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.Intune;

/// <summary>
/// Reads managed devices from Microsoft Graph with app-only auth. Needs the application permission
/// DeviceManagementManagedDevices.Read.All (read-only); see docs/intune-app-registration.md.
/// </summary>
public sealed partial class GraphIntuneClient(
    HttpClient http, TokenCredential credential, IOptions<IntuneOptions> options, ILogger<GraphIntuneClient> logger) : IIntuneClient
{
    private const string Fields =
        "id,deviceName,serialNumber,manufacturer,model,operatingSystem,userPrincipalName,complianceState,lastSyncDateTime,enrolledDateTime";

    private static readonly TokenRequestContext GraphScope = new(["https://graph.microsoft.com/.default"]);

    public async IAsyncEnumerable<IntuneDevice> GetManagedDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Uri? next = new(options.Value.GraphBaseUrl, $"deviceManagement/managedDevices?$select={Fields}");

        while (next is not null)
        {
            using var response = await SendWithRetryAsync(next, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<GraphPage>(JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Graph returned an empty page.");

            foreach (var device in page.Value)
                yield return device;

            next = page.NextLink is null ? null : new Uri(page.NextLink);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await credential.GetTokenAsync(GraphScope, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

            if (!retryable || attempt > options.Value.MaxRetries)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                throw new HttpRequestException(
                    $"Graph returned {(int)response.StatusCode} {response.StatusCode} after {attempt} attempt(s): {Truncate(body)}",
                    null, response.StatusCode);
            }

            // Graph tells us how long to back off; otherwise use exponential backoff.
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            LogRetry(logger, (int)response.StatusCode, delay.TotalSeconds, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "...";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph returned {Status}; retrying in {Seconds}s (attempt {Attempt}).")]
    private static partial void LogRetry(ILogger logger, int status, double seconds, int attempt);
}
