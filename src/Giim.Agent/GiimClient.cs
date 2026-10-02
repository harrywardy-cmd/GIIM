using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Giim.Agent;

/// <summary>A job from GIIM: the step and its parameters (fixed when it was queued).</summary>
internal sealed record AgentJob(Guid Id, string Step, string Parameters, bool DryRun, int Attempt);

internal sealed record CheckInReply(int PollSeconds, bool DryRun);

/// <summary>How a job went. Result is what GIIM records (e.g. the account created); Log is a short, secret-free account of what was done.</summary>
internal sealed record JobOutcome(bool Succeeded, object? Result, string? Error, string? Log, bool Retryable);

/// <summary>Talks to GIIM: check in, take jobs, report results. Outbound HTTPS only.</summary>
internal sealed class GiimClient(HttpClient http, IOptions<AgentOptions> options)
{
    private AgentOptions O => options.Value;

    public async Task<CheckInReply> CheckInAsync(string directory, string version, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("agent/check-in",
            new { name = O.EffectiveName, version, directory, dryRun = O.DryRun }, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CheckInReply>(JsonSerializerOptions.Web, cancellationToken)
            ?? new CheckInReply(15, true);
    }

    public async Task<IReadOnlyList<AgentJob>> ClaimAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("agent/jobs/claim", new { name = O.EffectiveName, max = O.MaxJobsPerPoll }, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<AgentJob>>(JsonSerializerOptions.Web, cancellationToken) ?? [];
    }

    public async Task ReportAsync(Guid jobId, JobOutcome outcome, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"agent/jobs/{jobId}/result", new
        {
            name = O.EffectiveName,
            outcome.Succeeded,
            outcome.Result,
            outcome.Error,
            outcome.Log,
            outcome.Retryable,
        }, JsonSerializerOptions.Web, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"GIIM returned {(int)response.StatusCode}: {(body.Length > 300 ? body[..300] : body)}", null, response.StatusCode);
    }
}

/// <summary>Adds the agent's credential to every request: the shared key, or an Entra token from its certificate.</summary>
internal sealed class GiimAuthHandler(IOptions<AgentOptions> options) : DelegatingHandler
{
    private TokenCredential? _credential;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (o.Auth == AgentAuth.Key)
        {
            request.Headers.Add("X-GIIM-Agent-Key", o.Key);
        }
        else
        {
            _credential ??= new ClientCertificateCredential(o.TenantId, o.ClientId, FindCertificate(o.CertificateThumbprint!));
            var token = await _credential.GetTokenAsync(new TokenRequestContext([$"api://{o.GiimClientId}/.default"]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>The agent's certificate from the computer's store; its private key never leaves this server.</summary>
    private static X509Certificate2 FindCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", "", StringComparison.Ordinal), validOnly: false);
        return found.Count == 1 ? found[0] : throw new InvalidOperationException($"Certificate {thumbprint} isn't in LocalMachine\\My.");
    }
}
