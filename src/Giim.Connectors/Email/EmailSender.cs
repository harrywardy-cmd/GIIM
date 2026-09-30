using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Giim.Connectors.Email;

public enum EmailMode
{
    /// <summary>Emails aren't sent (they wait in the outbox until a mode is chosen, then expire after 3 days).</summary>
    None,

    /// <summary>Each email is written as an .eml file (open it in Outlook or any mail app). For development.</summary>
    File,

    /// <summary>Sent from a Microsoft 365 mailbox through Microsoft Graph, as the app's managed identity.</summary>
    Graph,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailMode Mode { get; set; } = EmailMode.None;

    /// <summary>Where File mode writes emails; relative paths are under the repository (or app) folder.</summary>
    public string PickupDirectory { get; set; } = "artifacts/mail";

    /// <summary>The mailbox emails come from in Graph mode, e.g. giim@company.com.au (a shared mailbox is fine).</summary>
    public string? FromMailbox { get; set; }

    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");
}

public sealed record EmailMessage(string To, string? ToName, string Subject, string Text, string Html);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Writes each email to a file, so notifications can be read and checked on a developer PC.</summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var directory = FolderFor(options.Value.PickupDirectory);
        Directory.CreateDirectory(directory);

        var now = clock.GetUtcNow();
        var boundary = "giim-" + Guid.NewGuid().ToString("N");
        var eml = new StringBuilder()
            .Append("From: GIIM <giim@localhost>\r\n")
            .Append(CultureInfo.InvariantCulture, $"To: {Header(message.ToName)} <{message.To}>\r\n")
            .Append(CultureInfo.InvariantCulture, $"Subject: {Header(message.Subject)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Date: {now.ToString("r", CultureInfo.InvariantCulture)}\r\n")
            .Append("MIME-Version: 1.0\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Type: multipart/alternative; boundary=\"{boundary}\"\r\n\r\n")
            .Append(CultureInfo.InvariantCulture, $"--{boundary}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{message.Text}\r\n")
            .Append(CultureInfo.InvariantCulture, $"--{boundary}\r\nContent-Type: text/html; charset=utf-8\r\n\r\n{message.Html}\r\n")
            .Append(CultureInfo.InvariantCulture, $"--{boundary}--\r\n");

        var file = Path.Combine(directory, $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.eml");
        await File.WriteAllTextAsync(file, eml.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    // Encoded so names with accents ("Zoë") survive in the header.
    private static string Header(string? text) =>
        string.IsNullOrEmpty(text) ? "" : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}?=";

    /// <summary>A relative folder is placed next to the repository's samples folder when there is one (development).</summary>
    private static string FolderFor(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "samples")) && File.Exists(Path.Combine(dir.FullName, "Giim.slnx")))
                return Path.Combine(dir.FullName, path);
        return Path.Combine(AppContext.BaseDirectory, path);
    }
}

/// <summary>
/// Sends through Microsoft Graph (POST /users/{mailbox}/sendMail) as the app's managed identity. Needs the Mail.Send
/// application permission, limited to the GIIM mailbox by an Exchange Online policy (docs/email-notifications.md).
/// </summary>
public sealed class GraphEmailSender(HttpClient http, TokenCredential credential, IOptions<EmailOptions> options) : IEmailSender
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var mailbox = options.Value.FromMailbox;
        if (string.IsNullOrWhiteSpace(mailbox))
            throw new InvalidOperationException("Email:FromMailbox is not set.");

        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Value.GraphBaseUrl, $"users/{Uri.EscapeDataString(mailbox)}/sendMail"))
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = message.Subject,
                    body = new { contentType = "HTML", content = message.Html },
                    toRecipients = new[] { new { emailAddress = new { address = message.To, name = message.ToName } } },
                },
                saveToSentItems = true,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Graph sendMail failed ({(int)response.StatusCode}): {(body.Length > 300 ? body[..300] : body)}");
        }
    }
}
