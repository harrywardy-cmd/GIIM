using System.Globalization;
using Giim.Domain.Assets;
using Giim.Domain.People;
using Giim.Domain.Repairs;
using Giim.Domain.Requests;
using Giim.Infrastructure.Persistence;
using Giim.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Reports;

/// <summary>Builds the reports from the register, timelines, repairs, people and stock ledger.</summary>
public sealed class ReportService(GiimDbContext db, StockService stock, TimeProvider clock)
{
    private static readonly CultureInfo Australian = CultureInfo.GetCultureInfo("en-AU");
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public Task<Report> BuildAsync(string key, ReportFilter filter, CancellationToken cancellationToken) => key switch
    {
        "inventory" => InventoryAsync(filter, cancellationToken),
        "warranty" => WarrantyAsync(filter, cancellationToken),
        "repairs" => RepairsAsync(filter, cancellationToken),
        "technicians" => TechniciansAsync(filter, cancellationToken),
        "leavers" => LeaversAsync(cancellationToken),
        "stock" => StockAsync(cancellationToken),
        "requests" => RequestsAsync(filter, cancellationToken),
        _ => throw new KeyNotFoundException($"There is no '{key}' report."),
    };

    /// <summary>The period for activity reports: the given dates, or the last 90 days.</summary>
    public (DateOnly From, DateOnly To) Period(ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var to = filter.To ?? Today;
        var from = filter.From ?? to.AddDays(-90);
        return from <= to ? (from, to) : (to, from);
    }

    // ---- Asset inventory ------------------------------------------------------------------------------------

    private async Task<Report> InventoryAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Assets.AsNoTracking();
        query = filter.Status is { } status ? query.Where(a => a.Status == status) : query.Where(a => a.Status != AssetStatus.Disposed);
        if (filter.CategoryId is { } category) query = query.Where(a => a.CategoryId == category);
        if (filter.LocationId is { } location) query = query.Where(a => a.LocationId == location);

        var assets = await query
            .OrderBy(a => a.AssetTag ?? a.SerialNumber)
            .Select(a => new
            {
                a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name, a.Status,
                Location = db.Locations.Where(l => l.Id == a.LocationId).Select(l => l.Name).FirstOrDefault(),
                Holder = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.DisplayName).FirstOrDefault(),
                Department = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.Department!.Name).FirstOrDefault(),
                a.LegacyAssignedTo, a.PurchaseDate, a.WarrantyExpiry, a.Cost, a.LastSeenInIntune,
            })
            .ToListAsync(cancellationToken);

        int Count(params AssetStatus[] statuses) => assets.Count(a => statuses.Contains(a.Status));

        return new Report("inventory", "Asset inventory", "Every asset with status, location and holder. Disposed assets are left out unless you filter for them.",
            [
                new("Assets", N(assets.Count)),
                new("Assigned", N(Count(AssetStatus.Assigned, AssetStatus.ReturnRequested))),
                new("Ready to deploy", N(Count(AssetStatus.ReadyToDeploy))),
                new("In repair", N(Count(AssetStatus.InRepair))),
                new("Lost / stolen", N(Count(AssetStatus.Lost, AssetStatus.Stolen))),
                new("Purchase value", Money(assets.Sum(a => a.Cost ?? 0))),
            ],
            [
                new("assetTag", "Asset tag"), new("serial", "Serial"), new("manufacturer", "Manufacturer"), new("model", "Model"),
                new("category", "Category"), new("status", "Status"), new("location", "Location"), new("holder", "Assigned to"),
                new("department", "Department"), new("purchased", "Purchased", ColumnType.Date), new("warranty", "Warranty ends", ColumnType.Date),
                new("cost", "Cost", ColumnType.Money), new("lastSeen", "Last seen in Intune", ColumnType.DateTime),
            ],
            assets.Select(a => Row(
                ("assetTag", a.AssetTag), ("serial", a.SerialNumber), ("manufacturer", a.Manufacturer), ("model", a.Model),
                ("category", a.Category), ("status", StatusText(a.Status)), ("location", a.Location),
                ("holder", a.Holder ?? (a.LegacyAssignedTo is null ? null : a.LegacyAssignedTo + " (not linked)")),
                ("department", a.Department), ("purchased", a.PurchaseDate), ("warranty", a.WarrantyExpiry),
                ("cost", a.Cost), ("lastSeen", a.LastSeenInIntune))).ToList(),
            "Assets by category",
            [.. assets.GroupBy(a => a.Category).OrderByDescending(g => g.Count()).Select(g => new ChartBar(g.Key, g.Count()))]);
    }

    // ---- Warranty -------------------------------------------------------------------------------------------

    private async Task<Report> WarrantyAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var today = Today;
        var horizon = today.AddDays(Math.Clamp(filter.Days, 1, 3650));
        var inService = await db.Assets.AsNoTracking()
            .Where(a => a.Status != AssetStatus.Retired && a.Status != AssetStatus.Disposed)
            .Select(a => new
            {
                a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name, a.Status, a.WarrantyExpiry,
                Holder = db.People.Where(p => p.Id == a.AssignedToPersonId).Select(p => p.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var expiring = inService.Where(a => a.WarrantyExpiry >= today && a.WarrantyExpiry <= horizon).OrderBy(a => a.WarrantyExpiry).ToList();
        var expired = inService.Where(a => a.WarrantyExpiry < today).OrderByDescending(a => a.WarrantyExpiry).ToList();
        var noDate = inService.Count(a => a.WarrantyExpiry is null);

        return new Report("warranty", "Warranty", $"In-service assets whose warranty ends in the next {filter.Days} days, followed by those already out of warranty.",
            [
                new($"Expiring in {filter.Days} days", N(expiring.Count)),
                new("Out of warranty", N(expired.Count)),
                new("No warranty date recorded", N(noDate)),
                new("Covered beyond that", N(inService.Count - expiring.Count - expired.Count - noDate)),
            ],
            [
                new("state", "Warranty"), new("warranty", "Ends", ColumnType.Date), new("daysLeft", "Days left", ColumnType.Number),
                new("assetTag", "Asset tag"), new("serial", "Serial"), new("device", "Device"), new("category", "Category"),
                new("status", "Status"), new("holder", "Assigned to"),
            ],
            expiring.Select(a => (a, "Expiring")).Concat(expired.Select(a => (a, "Expired")))
                .Select(x => Row(
                    ("state", x.Item2), ("warranty", x.a.WarrantyExpiry), ("daysLeft", x.a.WarrantyExpiry!.Value.DayNumber - today.DayNumber),
                    ("assetTag", x.a.AssetTag), ("serial", x.a.SerialNumber), ("device", $"{x.a.Manufacturer} {x.a.Model}"),
                    ("category", x.a.Category), ("status", StatusText(x.a.Status)), ("holder", x.a.Holder)))
                .ToList(),
            "Out of warranty by category",
            [.. expired.GroupBy(a => a.Category).OrderByDescending(g => g.Count()).Select(g => new ChartBar(g.Key, g.Count()))]);
    }

    // ---- Repairs --------------------------------------------------------------------------------------------

    private async Task<Report> RepairsAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var (from, to) = Period(filter);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var repairs = await (
                from r in db.Repairs.AsNoTracking()
                join a in db.Assets.AsNoTracking() on r.AssetId equals a.Id
                where r.OpenedAt >= start && r.OpenedAt < end
                orderby r.OpenedAt descending
                select new { r, a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model, Category = a.Category!.Name })
            .ToListAsync(cancellationToken);

        var repeatAssets = await db.Repairs.AsNoTracking().GroupBy(r => r.AssetId).Where(g => g.Count() >= 2).Select(g => g.Key)
            .ToListAsync(cancellationToken);
        var repeat = repeatAssets.ToHashSet();
        var completed = repairs.Where(x => !x.r.IsOpen).ToList();

        return new Report("repairs", "Repairs", $"Repairs opened {D(from)} to {D(to)}.",
            [
                new("Repairs opened", N(repairs.Count)),
                new("Completed", N(completed.Count)),
                new("Total cost", Money(repairs.Sum(x => x.r.Cost ?? 0))),
                new("Average turnaround", completed.Count == 0 ? "-" : $"{completed.Average(x => x.r.Duration!.Value.TotalDays):0.0} days"),
                new("Warranty claims", N(repairs.Count(x => x.r.WarrantyClaim))),
                new("Beyond repair", N(repairs.Count(x => x.r.Outcome == RepairOutcome.BeyondRepair))),
                new("Assets repaired 2+ times (all time)", N(repeat.Count)),
            ],
            [
                new("opened", "Opened", ColumnType.Date), new("assetTag", "Asset tag"), new("device", "Device"), new("category", "Category"),
                new("fault", "Fault"), new("repairer", "Repaired by"), new("warranty", "Warranty claim"), new("vendorRef", "Vendor ref"),
                new("outcome", "Outcome"), new("work", "Work done"), new("completed", "Completed", ColumnType.Date),
                new("days", "Days", ColumnType.Number), new("cost", "Cost", ColumnType.Money), new("ticket", "Ticket"), new("repeat", "Repeat asset"),
            ],
            repairs.Select(x => Row(
                ("opened", DateOnly.FromDateTime(x.r.OpenedAt.UtcDateTime)), ("assetTag", x.AssetTag ?? x.SerialNumber),
                ("device", $"{x.Manufacturer} {x.Model}"), ("category", x.Category), ("fault", x.r.Fault),
                ("repairer", x.r.Vendor ?? "IT (internal)"), ("warranty", x.r.WarrantyClaim ? "Yes" : "No"), ("vendorRef", x.r.VendorReference),
                ("outcome", x.r.IsOpen ? "In progress" : x.r.Outcome == RepairOutcome.BeyondRepair ? "Beyond repair" : "Repaired"),
                ("work", x.r.WorkPerformed),
                ("completed", x.r.CompletedAt is { } c ? DateOnly.FromDateTime(c.UtcDateTime) : (DateOnly?)null),
                ("days", x.r.Duration is { } d ? Math.Round((decimal)d.TotalDays, 1) : (decimal?)null),
                ("cost", x.r.Cost), ("ticket", x.r.TicketNumber), ("repeat", repeat.Contains(x.Id) ? "Yes" : "")))
                .ToList(),
            "Repairs by category",
            [.. repairs.GroupBy(x => x.Category).OrderByDescending(g => g.Count()).Select(g => new ChartBar(g.Key, g.Count()))]);
    }

    // ---- Technician activity --------------------------------------------------------------------------------

    private async Task<Report> TechniciansAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var (from, to) = Period(filter);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var events = await db.AssetEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end && e.Actor != "system")
            .Where(e => e.Type != AssetEventType.Imported && e.Type != AssetEventType.OwnerLinked)   // bulk jobs, not hands-on work
            .GroupBy(e => new { e.Actor, e.Type })
            .Select(g => new { g.Key.Actor, g.Key.Type, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var stockMoves = await db.StockMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= start && m.CreatedAt < end)
            .GroupBy(m => m.Actor)
            .Select(g => new { Actor = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Actor, x => x.Count, cancellationToken);

        static bool Is(AssetEventType type, params AssetEventType[] types) => types.Contains(type);
        var rows = events.GroupBy(e => e.Actor).Select(g =>
            {
                int Sum(params AssetEventType[] types) => g.Where(e => Is(e.Type, types)).Sum(e => e.Count);
                var assigned = Sum(AssetEventType.Assigned);
                var returned = Sum(AssetEventType.Returned);
                var repairs = Sum(AssetEventType.RepairStarted, AssetEventType.RepairCompleted);
                var wipes = Sum(AssetEventType.Wiped);
                var added = Sum(AssetEventType.Created);
                var total = g.Sum(e => e.Count);
                var stockCount = stockMoves.GetValueOrDefault(g.Key);
                return new { Actor = g.Key, assigned, returned, repairs, wipes, added, other = total - assigned - returned - repairs - wipes - added, stockCount, total = total + stockCount };
            })
            .Concat(stockMoves.Keys.Except(events.Select(e => e.Actor)).Select(actor =>
                new { Actor = actor, assigned = 0, returned = 0, repairs = 0, wipes = 0, added = 0, other = 0, stockCount = stockMoves[actor], total = stockMoves[actor] }))
            .OrderByDescending(r => r.total)
            .ToList();

        return new Report("technicians", "Technician activity", $"Actions recorded {D(from)} to {D(to)}. Bulk imports and owner linking are left out; they aren't hands-on work.",
            [
                new("Technicians active", N(rows.Count)),
                new("Actions recorded", N(rows.Sum(r => r.total))),
                new("Assignments", N(rows.Sum(r => r.assigned))),
                new("Returns", N(rows.Sum(r => r.returned))),
                new("Repair actions", N(rows.Sum(r => r.repairs))),
            ],
            [
                new("technician", "Technician"), new("assigned", "Assignments", ColumnType.Number), new("returned", "Returns", ColumnType.Number),
                new("repairs", "Repair actions", ColumnType.Number), new("wipes", "Wipes", ColumnType.Number), new("added", "Assets added", ColumnType.Number),
                new("stock", "Stock movements", ColumnType.Number), new("other", "Other", ColumnType.Number), new("total", "Total", ColumnType.Number),
            ],
            rows.Select(r => Row(("technician", r.Actor), ("assigned", r.assigned), ("returned", r.returned), ("repairs", r.repairs),
                ("wipes", r.wipes), ("added", r.added), ("stock", r.stockCount), ("other", r.other), ("total", r.total))).ToList(),
            "Actions by technician",
            [.. rows.Take(12).Select(r => new ChartBar(r.Actor, r.total))]);
    }

    // ---- Leavers holding kit --------------------------------------------------------------------------------

    private async Task<Report> LeaversAsync(CancellationToken cancellationToken)
    {
        var leavers = await db.People.AsNoTracking()
            .Where(p => (p.Status == PersonStatus.Left || p.Status == PersonStatus.Leaving) && db.Assets.Any(a => a.AssignedToPersonId == p.Id))
            .OrderBy(p => p.Status).ThenBy(p => p.EndDate).ThenBy(p => p.DisplayName)
            .Select(p => new
            {
                p.DisplayName, p.EmployeeId, p.UserPrincipalName, Department = p.Department!.Name, p.Status, p.EndDate,
                Manager = db.People.Where(m => m.Id == p.ManagerId).Select(m => m.DisplayName).FirstOrDefault(),
                Assets = db.Assets.Where(a => a.AssignedToPersonId == p.Id)
                    .Select(a => new { Label = a.AssetTag ?? a.SerialNumber, Category = a.Category!.Name, a.Cost }).ToList(),
            })
            .ToListAsync(cancellationToken);

        return new Report("leavers", "Leavers holding kit", "People who have left or are leaving and still have assets recorded against them. Use it to chase returns.",
            [
                new("Left, still holding kit", N(leavers.Count(l => l.Status == PersonStatus.Left))),
                new("Leaving, holding kit", N(leavers.Count(l => l.Status == PersonStatus.Leaving))),
                new("Assets outstanding", N(leavers.Sum(l => l.Assets.Count))),
                new("Value outstanding", Money(leavers.Sum(l => l.Assets.Sum(a => a.Cost ?? 0)))),
            ],
            [
                new("person", "Person"), new("employeeId", "Employee ID"), new("email", "Email"), new("department", "Department"),
                new("status", "Status"), new("endDate", "End date", ColumnType.Date), new("manager", "Manager"),
                new("count", "Assets", ColumnType.Number), new("assets", "Asset tags"),
            ],
            leavers.Select(l => Row(("person", l.DisplayName), ("employeeId", l.EmployeeId), ("email", l.UserPrincipalName),
                ("department", l.Department), ("status", l.Status.ToString()), ("endDate", l.EndDate), ("manager", l.Manager),
                ("count", l.Assets.Count), ("assets", string.Join(", ", l.Assets.Select(a => $"{a.Label} ({a.Category})"))))).ToList(),
            "Outstanding assets by department",
            [.. leavers.GroupBy(l => l.Department ?? "No department").Select(g => new ChartBar(g.Key, g.Sum(l => l.Assets.Count))).OrderByDescending(b => b.Value)]);
    }

    // ---- Stock levels ---------------------------------------------------------------------------------------

    private async Task<Report> StockAsync(CancellationToken cancellationToken)
    {
        var levels = await stock.GetLevelsAsync(cancellationToken);
        var active = levels.Where(l => l.IsActive).ToList();

        return new Report("stock", "Stock levels", "Items without serial numbers: stock on hand per location against the reorder level.",
            [
                new("Stock items", N(active.Count)),
                new("Low stock", N(active.Count(l => l.IsLow))),
                new("Units on hand", N(active.Sum(l => l.Total))),
            ],
            [
                new("item", "Item"), new("total", "On hand", ColumnType.Number), new("reorder", "Reorder at", ColumnType.Number),
                new("state", "State"), new("locations", "By location"),
            ],
            active.OrderByDescending(l => l.IsLow).ThenBy(l => l.Name).Select(l => Row(("item", l.Name), ("total", l.Total), ("reorder", l.ReorderLevel),
                ("state", l.IsLow ? "Low" : "OK"), ("locations", string.Join("; ", l.Locations.Select(x => $"{x.Location}: {x.Quantity}"))))).ToList(),
            "On hand by item",
            [.. active.OrderByDescending(l => l.Total).Take(12).Select(l => new ChartBar(l.Name, l.Total))]);
    }

    // ---- Device requests -----------------------------------------------------------------------------------

    private async Task<Report> RequestsAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var (from, to) = Period(filter);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var requests = await db.DeviceRequests.AsNoTracking()
            .Where(r => r.SubmittedAt >= start && r.SubmittedAt < end)
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new
            {
                r.Number, r.SubmittedAt, r.DeviceDescription, r.Status, r.Priority, r.ReasonType, r.RequestedByName, r.DecidedByName,
                r.DecidedAt, r.EstimatedCost, r.OrderCost, r.Supplier, r.PurchaseOrder, r.BudgetCode, r.CompletedAt,
                Category = db.AssetCategories.Where(c => c.Id == r.CategoryId).Select(c => c.Name).First(),
                Recipient = db.People.Where(p => p.Id == r.RecipientPersonId).Select(p => p.DisplayName).First(),
                Department = db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                Approver = db.People.Where(p => p.Id == r.ApproverPersonId).Select(p => p.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var decided = requests.Where(r => r.DecidedAt is not null).ToList();
        var approved = decided.Count(r => r.Status != RequestStatus.Rejected);
        var completed = requests.Where(r => r.CompletedAt is not null).ToList();
        static double Days(DateTimeOffset a, DateTimeOffset b) => (b - a).TotalDays;

        return new Report("requests", "Device requests", $"Requests raised {D(from)} to {D(to)}.",
            [
                new("Requests", N(requests.Count)),
                new("Waiting for approval", N(requests.Count(r => r.Status is RequestStatus.PendingApproval or RequestStatus.InfoRequested))),
                new("Approval rate", decided.Count == 0 ? "-" : $"{100.0 * approved / decided.Count:0}%"),
                new("Average time to decide", decided.Count == 0 ? "-" : $"{decided.Average(r => Days(r.SubmittedAt, r.DecidedAt!.Value)):0.0} days"),
                new("Average time to hand over", completed.Count == 0 ? "-" : $"{completed.Average(r => Days(r.SubmittedAt, r.CompletedAt!.Value)):0.0} days"),
                new("Ordered value", Money(requests.Sum(r => r.OrderCost ?? 0))),
            ],
            [
                new("reference", "Request"), new("submitted", "Raised", ColumnType.Date), new("device", "Device"), new("category", "Category"),
                new("recipient", "For"), new("department", "Department"), new("requestedBy", "Requested by"), new("status", "Status"),
                new("priority", "Priority"), new("reason", "Reason"), new("approver", "Approver"), new("decidedBy", "Decided by"),
                new("daysToDecide", "Days to decide", ColumnType.Number), new("budget", "Budget code"), new("estimated", "Estimated cost", ColumnType.Money),
                new("cost", "Order cost", ColumnType.Money), new("supplier", "Supplier"), new("po", "PO number"),
                new("daysToHandOver", "Days to hand over", ColumnType.Number),
            ],
            requests.Select(r => Row(
                ("reference", DeviceRequest.FormatReference(r.Number)), ("submitted", DateOnly.FromDateTime(r.SubmittedAt.UtcDateTime)),
                ("device", r.DeviceDescription), ("category", r.Category), ("recipient", r.Recipient), ("department", r.Department),
                ("requestedBy", r.RequestedByName), ("status", DeviceRequest.StatusText(r.Status)), ("priority", r.Priority.ToString()),
                ("reason", r.ReasonType.ToString()), ("approver", r.Approver ?? "Administrator"), ("decidedBy", r.DecidedByName),
                ("daysToDecide", r.DecidedAt is { } d ? Math.Round((decimal)Days(r.SubmittedAt, d), 1) : (decimal?)null),
                ("budget", r.BudgetCode), ("estimated", r.EstimatedCost), ("cost", r.OrderCost), ("supplier", r.Supplier), ("po", r.PurchaseOrder),
                ("daysToHandOver", r.CompletedAt is { } c ? Math.Round((decimal)Days(r.SubmittedAt, c), 1) : (decimal?)null))).ToList(),
            "Requests by status",
            [.. requests.GroupBy(r => DeviceRequest.StatusText(r.Status)).OrderByDescending(g => g.Count()).Select(g => new ChartBar(g.Key, g.Count()))]);
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value);

    private static string N(int value) => value.ToString("N0", Australian);
    private static string Money(decimal value) => value.ToString("C0", Australian);
    private static string D(DateOnly date) => date.ToString("d MMM yyyy", Australian);

    public static string StatusText(AssetStatus status) => status switch
    {
        AssetStatus.ReadyToDeploy => "Ready to deploy",
        AssetStatus.ReturnRequested => "Return requested",
        AssetStatus.InRepair => "In repair",
        _ => status.ToString(),
    };
}
