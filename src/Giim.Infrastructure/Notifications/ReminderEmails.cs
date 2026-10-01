using System.Globalization;
using System.Net;
using System.Text;
using Giim.Domain.Notifications;

namespace Giim.Infrastructure.Notifications;

/// <summary>A heading and its lines in an email (for example "Starting soon" and one line per starter).</summary>
public sealed record EmailSection(string Heading, IReadOnlyList<string> Lines);

/// <summary>
/// Writes reminder and digest emails: an introduction, sections of short lines, and one link. Everything is
/// HTML-encoded, so names and notes people typed can't inject content into an inbox.
/// </summary>
public static class ReminderEmails
{
    public static readonly CultureInfo Australian = CultureInfo.GetCultureInfo("en-AU");

    public static string Date(DateOnly? date) => date?.ToString("ddd d MMM yyyy", Australian) ?? "no date";

    public static Notification Compose(string kind, string to, string? toName, string subject, string intro,
        IReadOnlyList<EmailSection> sections, string? actionText, string? actionUrl, DateTimeOffset now, Guid? requestId = null)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Hello {toName ?? "there"},").AppendLine()
            .AppendLine(intro);
        var html = new StringBuilder()
            .Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2330;max-width:640px\">")
            .Append(CultureInfo.InvariantCulture, $"<p>Hello {E(toName ?? "there")},</p><p>{E(intro)}</p>");

        foreach (var section in sections.Where(s => s.Lines.Count > 0))
        {
            text.AppendLine().AppendLine(section.Heading);
            html.Append(CultureInfo.InvariantCulture, $"<h3 style=\"font-size:15px;margin:18px 0 6px\">{E(section.Heading)}</h3><ul style=\"margin:0;padding-left:20px\">");
            foreach (var line in section.Lines)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
                html.Append(CultureInfo.InvariantCulture, $"<li style=\"margin:3px 0\">{E(line)}</li>");
            }
            html.Append("</ul>");
        }

        if (actionUrl is not null)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{actionText}: {actionUrl}");
            html.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin-top:18px\"><a href=\"{E(actionUrl)}\" style=\"display:inline-block;background:#6c47ff;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none\">{E(actionText)}</a></p>");
        }

        text.AppendLine().AppendLine("This email was sent by GIIM, the IT asset system. Please don't reply to it.");
        html.Append("<p style=\"color:#667085;font-size:12px\">This email was sent by GIIM, the IT asset system. Please don't reply to it.</p></div>");
        return Notification.Create(kind, to, toName, subject, text.ToString(), html.ToString(), requestId, now);
    }
}
