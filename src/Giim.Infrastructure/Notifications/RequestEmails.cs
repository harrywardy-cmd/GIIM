using System.Globalization;
using System.Net;
using System.Text;
using Giim.Domain.Notifications;
using Giim.Domain.Requests;

namespace Giim.Infrastructure.Notifications;

/// <summary>What a request email is about, with everything needed to write it.</summary>
public sealed record RequestEmailFacts(
    DeviceRequest Request,
    string RecipientName,
    string? DepartmentName,
    string Link);

/// <summary>
/// Writes the device-request emails: short, with the key facts and one link to the request in GIIM. Everything
/// people typed is HTML-encoded, so text in a request can't inject content into someone's inbox.
/// </summary>
public static class RequestEmails
{
    private static readonly CultureInfo Australian = CultureInfo.GetCultureInfo("en-AU");

    public static Notification ApprovalNeeded(RequestEmailFacts facts, string to, string? toName, DateTimeOffset now) =>
        Build("RequestApprovalNeeded", facts, to, toName, now,
            subject: $"Approval needed: {facts.Request.Reference} {facts.Request.DeviceDescription} for {facts.RecipientName}",
            intro: $"{facts.Request.RequestedByName} has requested a device and needs your approval.",
            action: "Review and approve");

    public static Notification InformationProvided(RequestEmailFacts facts, string to, string? toName, string answer, DateTimeOffset now) =>
        Build("RequestInfoProvided", facts, to, toName, now,
            subject: $"Information provided: {facts.Request.Reference} is waiting for your approval",
            intro: $"{facts.Request.RequestedByName} answered your question: “{answer}”",
            action: "Review and approve");

    public static Notification InformationRequested(RequestEmailFacts facts, string to, string question, string askedBy, DateTimeOffset now) =>
        Build("RequestInfoRequested", facts, to, facts.Request.RequestedByName, now,
            subject: $"More information needed: {facts.Request.Reference} {facts.Request.DeviceDescription}",
            intro: $"{askedBy} needs more information before approving: “{question}”",
            action: "Answer in GIIM");

    public static Notification Decided(RequestEmailFacts facts, string to, DateTimeOffset now)
    {
        var request = facts.Request;
        var approved = request.Status == RequestStatus.Approved;
        return Build(approved ? "RequestApproved" : "RequestRejected", facts, to, request.RequestedByName, now,
            subject: $"{(approved ? "Approved" : "Rejected")}: {request.Reference} {request.DeviceDescription} for {facts.RecipientName}",
            intro: approved
                ? $"{request.DecidedByName} approved the request.{(request.DecisionComment is { } c ? $" “{c}”" : "")} IT will arrange the device."
                : $"{request.DecidedByName} rejected the request: “{request.DecisionComment}”",
            action: "View the request");
    }

    public static Notification HandedOver(RequestEmailFacts facts, string to, string assetName, DateTimeOffset now) =>
        Build("RequestCompleted", facts, to, facts.Request.RequestedByName, now,
            subject: $"Completed: {facts.Request.Reference} {assetName} handed over to {facts.RecipientName}",
            intro: $"{assetName} has been assigned to {facts.RecipientName}. The request is complete.",
            action: "View the request");

    private static Notification Build(string kind, RequestEmailFacts facts, string to, string? toName, DateTimeOffset now,
        string subject, string intro, string action)
    {
        var r = facts.Request;
        var rows = new List<(string Label, string? Value)>
        {
            ("Request", r.Reference),
            ("Device", r.DeviceDescription),
            ("For", facts.RecipientName + (facts.DepartmentName is null ? "" : $" ({facts.DepartmentName})")),
            ("Reason", r.Reason),
            ("Priority", r.Priority.ToString()),
            ("Specifications", r.Specifications),
            ("Needed by", r.NeededBy?.ToString("d MMMM yyyy", Australian)),
            ("Estimated cost", r.EstimatedCost?.ToString("C", Australian)),
            ("Requested by", r.RequestedByName),
        };
        var shown = rows.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToList();

        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Hello {toName ?? "there"},").AppendLine()
            .AppendLine(intro).AppendLine();
        foreach (var (label, value) in shown) text.AppendLine(CultureInfo.InvariantCulture, $"{label}: {value}");
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{action}: {facts.Link}")
            .AppendLine().AppendLine("This email was sent by GIIM, the IT asset system. Please don't reply to it.");

        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var html = new StringBuilder()
            .Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2330;max-width:560px\">")
            .Append(CultureInfo.InvariantCulture, $"<p>Hello {E(toName ?? "there")},</p><p>{E(intro)}</p>")
            .Append("<table style=\"border-collapse:collapse;margin:12px 0\">");
        foreach (var (label, value) in shown)
            html.Append(CultureInfo.InvariantCulture,
                $"<tr><td style=\"padding:4px 16px 4px 0;color:#667085\">{E(label)}</td><td style=\"padding:4px 0\">{E(value)}</td></tr>");
        html.Append("</table>")
            .Append(CultureInfo.InvariantCulture,
                $"<p><a href=\"{E(facts.Link)}\" style=\"display:inline-block;background:#6c47ff;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none\">{E(action)}</a></p>")
            .Append("<p style=\"color:#667085;font-size:12px\">This email was sent by GIIM, the IT asset system. Please don't reply to it.</p></div>");

        return Notification.Create(kind, to, toName, subject, text.ToString(), html.ToString(), r.Id, now);
    }
}
