using Giim.Domain.People;
using Giim.Domain.Requests;
using Giim.Infrastructure.Notifications;

namespace Giim.Infrastructure.Tests;

public class RequestEmailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(10));
    private static readonly Person Manager = new() { EmployeeId = "E1", DisplayName = "Emily Carter", DepartmentId = Guid.NewGuid() };
    private static readonly Person Recipient = new() { EmployeeId = "E2", DisplayName = "Tom Harris", DepartmentId = Manager.DepartmentId, Status = PersonStatus.Active };
    private static readonly RequestActor Technician = new("tina@co", "Tina Tech", "tina@co", null, false, true, false);

    private static RequestEmailFacts Facts(string reason = "New starter in Sales")
    {
        var (request, _) = DeviceRequest.Submit(1028,
            new NewDeviceRequest(Recipient.Id, Guid.NewGuid(), "Dell Latitude 7455", RequestReason.NewStarter, reason, RequestPriority.High,
                EstimatedCost: 1849m, NeededBy: new DateOnly(2026, 10, 12)),
            Recipient, Manager.Id, Technician, Now);
        return new RequestEmailFacts(request, "Tom Harris", "Sales", $"https://giim.example/?request={request.Id}");
    }

    [Fact]
    public void Approval_email_names_the_request_and_links_to_it()
    {
        var facts = Facts();

        var email = RequestEmails.ApprovalNeeded(facts, "emily@co", "Emily Carter", Now);

        Assert.Equal("emily@co", email.ToAddress);
        Assert.Equal("Approval needed: REQ1028 Dell Latitude 7455 for Tom Harris", email.Subject);
        Assert.Contains(facts.Link, email.BodyText, StringComparison.Ordinal);
        Assert.Contains($"href=\"{facts.Link}\"", email.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("Needed by: 12 October 2026", email.BodyText, StringComparison.Ordinal);
        Assert.Contains("Estimated cost: $1,849.00", email.BodyText, StringComparison.Ordinal);
        Assert.Equal(facts.Request.Id, email.RequestId);
    }

    [Fact]
    public void Text_people_typed_cannot_inject_html()
    {
        var email = RequestEmails.ApprovalNeeded(Facts("<a href=\"https://evil.example\">click</a>"), "emily@co", "Emily Carter", Now);

        Assert.DoesNotContain("<a href=\"https://evil.example\"", email.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;a href=&quot;https://evil.example&quot;&gt;", email.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejection_email_carries_the_reason()
    {
        var facts = Facts();
        var approver = new RequestActor("emily@co", "Emily Carter", "emily@co", Manager.Id, false, false, true);
        facts.Request.Reject(approver, Now, "Existing laptop available in inventory.");

        var email = RequestEmails.Decided(facts, "tina@co", Now);

        Assert.StartsWith("Rejected: REQ1028", email.Subject, StringComparison.Ordinal);
        Assert.Contains("Existing laptop available in inventory.", email.BodyText, StringComparison.Ordinal);
        Assert.Equal("RequestRejected", email.Kind);
    }
}
