using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Giim.Connectors.Graph;

/// <summary>
/// Reads every page of a Microsoft Graph list with app-only auth, backing off when Graph throttles. Shared by the
/// Graph connectors (Intune devices, staff directory); read-only.
/// </summary>
internal static partial class GraphReader
{
    private static readonly TokenRequestContext GraphScope = new(["https://graph.microsoft.com/.default"]);

    public static async IAsyncEnumerable<T> ReadAllAsync<T>(HttpClient http, TokenCredential credential, Uri first, int maxRetries,
        ILogger logger, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Uri? next = first;
        while (next is not null)
        {
            using var response = await SendWithRetryAsync(http, credential, next, maxRetries, logger, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<GraphPage<T>>(JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Graph returned an empty page.");

            foreach (var item in page.Value)
                yield return item;

            next = page.NextLink is null ? null : new Uri(page.NextLink);
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient http, TokenCredential credential, Uri url, int maxRetries,
        ILogger logger, CancellationToken cancellationToken)
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

            if (!retryable || attempt > maxRetries)
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

/// <summary>One page of a Graph list.</summary>
internal sealed record GraphPage<T>(
    [property: JsonPropertyName("value")] IReadOnlyList<T> Value,
    [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
