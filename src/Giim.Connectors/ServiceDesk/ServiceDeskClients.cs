using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.ServiceDesk;

/// <summary>
/// Access tokens for ServiceDesk Plus Cloud from Zoho, using the long-lived refresh token (a "self client" in the
/// Zoho API console). Tokens last an hour; one is shared and renewed five minutes before it expires.
/// </summary>
public sealed class ZohoTokenProvider(IHttpClientFactory httpFactory, IOptions<ServiceDeskOptions> options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expires;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (token is not null && clock.GetUtcNow() < expires) return token;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (token is not null && clock.GetUtcNow() < expires) return token;
            var o = options.Value;
            if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.ClientSecret) || string.IsNullOrWhiteSpace(o.RefreshToken))
                throw new InvalidOperationException("ServiceDesk Plus sign-in isn't configured (ServiceDesk:ClientId, ClientSecret, RefreshToken).");

            using var http = httpFactory.CreateClient(nameof(ZohoTokenProvider));
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = o.RefreshToken,
                ["client_id"] = o.ClientId,
                ["client_secret"] = o.ClientSecret,
                ["grant_type"] = "refresh_token",
            });
            using var response = await http.PostAsync(new Uri(o.ZohoAccountsUrl, "oauth/v2/token"), form, cancellationToken);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("access_token", out var accessToken))
                throw new HttpRequestException($"Zoho sign-in failed ({(int)response.StatusCode}): "
                    + (json.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "no access token"));

            token = accessToken.GetString();
            var lifetime = json.RootElement.TryGetProperty("expires_in", out var seconds) ? seconds.GetInt32() : 3600;
            expires = clock.GetUtcNow().AddSeconds(Math.Max(60, lifetime - 300));
            return token!;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}

/// <summary>ServiceDesk Plus Cloud REST API v3 (…/app/{portal}/api/v3/).</summary>
public sealed class ApiServiceDeskClient(HttpClient http, ZohoTokenProvider tokens, IOptions<ServiceDeskOptions> options) : IServiceDeskClient
{
    private Uri Api => new(options.Value.SiteUrl, $"app/{Uri.EscapeDataString(options.Value.Portal)}/api/v3/");

    public async Task<ServiceDeskRequest?> GetRequestAsync(string requestKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"requests/{Uri.EscapeDataString(requestKey)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        using var json = await ReadAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("request", out var request) ? ServiceDeskJson.ParseRequest(request) : null;
    }

    public async Task<string?> FindRequestKeyAsync(string displayId, CancellationToken cancellationToken = default)
    {
        var inputData = JsonSerializer.Serialize(new
        {
            list_info = new { row_count = 1, search_criteria = new { field = "display_id", condition = "is", value = displayId } },
        });
        using var response = await SendAsync(HttpMethod.Get, $"requests?input_data={Uri.EscapeDataString(inputData)}", null, cancellationToken);
        using var json = await ReadAsync(response, cancellationToken);
        return json.RootElement.TryGetProperty("requests", out var requests) && requests.GetArrayLength() > 0
            && requests[0].TryGetProperty("id", out var id)
            ? id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText()
            : null;
    }

    public async Task AddNoteAsync(string requestKey, string html, CancellationToken cancellationToken = default)
    {
        var inputData = JsonSerializer.Serialize(new
        {
            note = new { description = html, show_to_requester = false, mark_first_response = false, add_to_linked_requests = false },
        });
        using var response = await SendAsync(HttpMethod.Post, $"requests/{Uri.EscapeDataString(requestKey)}/notes", inputData, cancellationToken);
        using var _ = await ReadAsync(response, cancellationToken);
    }

    public async Task ResolveAsync(string requestKey, string resolution, CancellationToken cancellationToken = default)
    {
        var inputData = JsonSerializer.Serialize(new
        {
            request = new { resolution = new { content = resolution }, status = new { name = "Resolved" } },
        });
        using var response = await SendAsync(HttpMethod.Put, $"requests/{Uri.EscapeDataString(requestKey)}", inputData, cancellationToken);
        using var _ = await ReadAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? inputData, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Api, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", await tokens.GetAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ServiceDeskJson.MediaType));
        // Changes are sent as a form field called input_data holding JSON, as the v3 API expects.
        if (inputData is not null)
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["input_data"] = inputData });
        return await http.SendAsync(request, cancellationToken);
    }

    /// <summary>The response body, or an error with SDP's own message (its status codes sit inside the body).</summary>
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument? json = null;
        try { json = JsonDocument.Parse(body); } catch (JsonException) { }

        var failed = !response.IsSuccessStatusCode
            || (json?.RootElement.TryGetProperty("response_status", out var status) == true && status.ValueKind == JsonValueKind.Object
                && status.TryGetProperty("status", out var state) && state.GetString() == "failed");
        if (!failed && json is not null) return json;

        json?.Dispose();
        var retryAfter = response.Headers.RetryAfter?.Delta is { } delta ? $" (retry after {delta.TotalSeconds:0}s)" : "";
        throw new HttpRequestException(
            $"ServiceDesk Plus returned {(int)response.StatusCode}{retryAfter}: {(body.Length > 300 ? body[..300] : body)}");
    }
}

/// <summary>A stand-in for development: tickets from a sample file, notes and resolutions written to a log file.</summary>
public sealed class FileServiceDeskClient(IOptions<ServiceDeskOptions> options, TimeProvider clock) : IServiceDeskClient
{
    public async Task<ServiceDeskRequest?> GetRequestAsync(string requestKey, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).FirstOrDefault(r => r.Key == requestKey);

    public async Task<string?> FindRequestKeyAsync(string displayId, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).FirstOrDefault(r => string.Equals(r.DisplayId, displayId, StringComparison.OrdinalIgnoreCase))?.Key;

    public Task AddNoteAsync(string requestKey, string html, CancellationToken cancellationToken = default) =>
        WriteAsync($"NOTE on {requestKey}: {html}", cancellationToken);

    public Task ResolveAsync(string requestKey, string resolution, CancellationToken cancellationToken = default) =>
        WriteAsync($"RESOLVED {requestKey}: {resolution}", cancellationToken);

    private async Task<IReadOnlyList<ServiceDeskRequest>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(SamplePath.Resolve(options.Value.SampleFilePath));
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return [.. json.RootElement.GetProperty("requests").EnumerateArray().Select(ServiceDeskJson.ParseRequest)];
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        var path = options.Value.NotesFilePath;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Path.GetDirectoryName(SamplePath.Resolve(options.Value.SampleFilePath))!, "..", path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path,
            $"{clock.GetUtcNow().ToString("u", CultureInfo.InvariantCulture)} {line}{Environment.NewLine}", Encoding.UTF8, cancellationToken);
    }
}
