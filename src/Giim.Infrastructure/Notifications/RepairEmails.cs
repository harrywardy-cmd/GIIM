using Giim.Domain.Notifications;
using Giim.Domain.Repairs;

namespace Giim.Infrastructure.Notifications;

/// <summary>The device and its holder, for a repair email.</summary>
public sealed record RepairEmailFacts(string Device, string? AssetTag, string HolderAddress, string HolderName, string? Link);

/// <summary>
/// Emails to the person whose device goes for repair: when it goes, and when it's done (repaired, or can't be). Saved in
/// the same step as the repair, like every other email (the outbox).
/// </summary>
public static class RepairEmails
{
    public static Notification Started(RepairEmailFacts facts, Repair repair, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(repair);
        var where = repair.Vendor is null ? "IT is repairing it" : $"it has gone to {repair.Vendor}" + (repair.WarrantyClaim ? " under warranty" : "");
        var lines = new List<string> { $"Fault: {repair.Fault}" };
        if (repair.VendorReference is { } reference) lines.Add($"Vendor reference: {reference}");
        if (repair.TicketNumber is { } ticket) lines.Add($"Ticket: {ticket}");
        return ReminderEmails.Compose("RepairStarted", facts.HolderAddress, facts.HolderName,
            $"Your {facts.Device} has gone for repair",
            $"Your {Name(facts)} is being repaired: {where}. You'll get another email when it's done. If you need a device in the meantime, contact the service desk.",
            [new EmailSection("Details", lines)], "View the device", facts.Link, now);
    }

    public static Notification Completed(RepairEmailFacts facts, Repair repair, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(repair);
        var repaired = repair.Outcome == RepairOutcome.Repaired;
        var lines = new List<string>();
        if (repaired && repair.WorkPerformed is { } work) lines.Add($"Work done: {work}");
        if (repair.Diagnosis is { } diagnosis) lines.Add($"Diagnosis: {diagnosis}");
        if (repair.TicketNumber is { } ticket) lines.Add($"Ticket: {ticket}");
        return ReminderEmails.Compose("RepairCompleted", facts.HolderAddress, facts.HolderName,
            repaired ? $"Your {facts.Device} is repaired" : $"Your {facts.Device} can't be repaired",
            repaired
                ? $"Your {Name(facts)} is repaired. The service desk will arrange for you to get it back."
                : $"Your {Name(facts)} can't be repaired. IT will arrange a replacement; contact the service desk if you need a device in the meantime.",
            [new EmailSection("Details", lines)], "View the device", facts.Link, now);
    }

    private static string Name(RepairEmailFacts f) => f.AssetTag is null ? f.Device : $"{f.Device} ({f.AssetTag})";
}
