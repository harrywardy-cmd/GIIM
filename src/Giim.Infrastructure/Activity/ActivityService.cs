using Giim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Giim.Infrastructure.Activity;

/// <summary>One thing that happened: an asset timeline entry or a stock movement.</summary>
public sealed record ActivityItem(
    string Kind,              // "Asset" or "Stock"
    long SortKey,
    DateTimeOffset OccurredAt,
    string Type,
    string Summary,
    string Actor,
    string? TicketNumber,
    Guid? AssetId,
    string? AssetLabel,
    string? Note);

public sealed record TicketSummary(
    string TicketNumber, int Actions, int Assets, DateTimeOffset FirstAt, DateTimeOffset LastAt, IReadOnlyList<string> Technicians);

/// <summary>
/// Everything GIIM recorded, looked at by ticket number or by technician. Asset timelines already cover
/// assignments, returns and repairs, so the feed is asset events plus stock movements.
/// </summary>
public sealed class ActivityService(GiimDbContext db)
{
    private const int MaxItems = 500;

    public static string? NormaliseTicket(string? ticket) =>
        string.IsNullOrWhiteSpace(ticket) ? null : ticket.Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<ActivityItem>> FeedAsync(string? ticket, string? actor, CancellationToken cancellationToken)
    {
        var ticketKey = NormaliseTicket(ticket);
        var actorKey = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();

        var assetEvents = await (
                from e in db.AssetEvents.AsNoTracking()
                join a in db.Assets.AsNoTracking() on e.AssetId equals a.Id
                where (ticketKey == null || e.TicketNumber == ticketKey) && (actorKey == null || e.Actor == actorKey)
                orderby e.OccurredAt descending, e.Id descending
                select new { e.Id, e.OccurredAt, e.Type, e.Summary, e.Actor, e.TicketNumber, e.Note, AssetId = a.Id, a.AssetTag, a.SerialNumber, a.Manufacturer, a.Model })
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        // Older stock tickets were stored as typed; the database collation is case-insensitive, so "inc1" matches "INC1".
        var stock = await (
                from m in db.StockMovements.AsNoTracking()
                join i in db.StockItems.AsNoTracking() on m.StockItemId equals i.Id
                where (ticketKey == null || m.ServiceDeskRequestId == ticketKey) && (actorKey == null || m.Actor == actorKey)
                orderby m.CreatedAt descending
                select new { m, i.Name })
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        var stockItems = stock.Select(x => new ActivityItem("Stock", 0, x.m.CreatedAt, "Stock" + x.m.Reason,
            $"{x.m.Reason} {Math.Abs(x.m.Quantity)} × {x.Name} ({(x.m.Quantity > 0 ? "+" : "")}{x.m.Quantity} at {x.m.Location})",
            x.m.Actor, NormaliseTicket(x.m.ServiceDeskRequestId), null, null, x.m.Note));

        var assetItems = assetEvents.Select(e => new ActivityItem("Asset", e.Id, e.OccurredAt, e.Type.ToString(), e.Summary, e.Actor,
            e.TicketNumber, e.AssetId, $"{e.AssetTag ?? e.SerialNumber} {e.Manufacturer} {e.Model}", e.Note));

        return assetItems.Concat(stockItems)
            .OrderByDescending(i => i.OccurredAt).ThenByDescending(i => i.SortKey)
            .Take(MaxItems)
            .ToList();
    }

    public async Task<(int Total, IReadOnlyList<TicketSummary> Tickets)> TicketsAsync(string? search, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var term = NormaliseTicket(search);

        var fromAssets = await db.AssetEvents.AsNoTracking()
            .Where(e => e.TicketNumber != null && (term == null || e.TicketNumber.Contains(term)))
            .Select(e => new { Ticket = e.TicketNumber!, e.OccurredAt, e.Actor, AssetId = (Guid?)e.AssetId })
            .ToListAsync(cancellationToken);
        var fromStock = await db.StockMovements.AsNoTracking()
            .Where(m => m.ServiceDeskRequestId != null && (term == null || m.ServiceDeskRequestId.Contains(term)))
            .Select(m => new { Ticket = m.ServiceDeskRequestId!, OccurredAt = m.CreatedAt, m.Actor, AssetId = (Guid?)null })
            .ToListAsync(cancellationToken);

        var tickets = fromAssets.Concat(fromStock)
            .GroupBy(x => x.Ticket.ToUpperInvariant())
            .Select(g => new TicketSummary(
                g.Key,
                g.Count(),
                g.Where(x => x.AssetId != null).Select(x => x.AssetId).Distinct().Count(),
                g.Min(x => x.OccurredAt),
                g.Max(x => x.OccurredAt),
                [.. g.Select(x => x.Actor).Distinct().Order(StringComparer.OrdinalIgnoreCase)]))
            .OrderByDescending(t => t.LastAt)
            .ToList();

        pageSize = Math.Clamp(pageSize, 1, 200);
        return (tickets.Count, tickets.Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToList());
    }

    /// <summary>Technicians (actors) whose name matches, with how much they've recorded.</summary>
    public async Task<IReadOnlyList<(string Actor, int Actions)>> TechniciansAsync(string search, int take, CancellationToken cancellationToken)
    {
        var term = search.Trim();
        var counts = await db.AssetEvents.AsNoTracking()
            .Where(e => e.Actor.Contains(term) && e.Actor != "system")
            .GroupBy(e => e.Actor)
            .Select(g => new { Actor = g.Key, Actions = g.Count() })
            .OrderByDescending(x => x.Actions)
            .Take(take)
            .ToListAsync(cancellationToken);
        return counts.Select(c => (c.Actor, c.Actions)).ToList();
    }
}
