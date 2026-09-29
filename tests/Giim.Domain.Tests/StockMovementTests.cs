using Giim.Domain.Common;
using Giim.Domain.Stock;

namespace Giim.Domain.Tests;

public class StockMovementTests
{
    private static readonly StockItem Bag = new() { Name = "Laptop bag", ReorderLevel = 10 };

    private static StockMovement Record(StockMovementReason reason, int quantity, int current = 20, string? note = null) =>
        StockMovement.Record(Bag, Store, reason, quantity, current, "tester", note);

    private static readonly Locations.Location Store = new() { Name = "IT Store Room", HoldsStock = true };

    [Fact]
    public void Movement_links_to_the_location_and_keeps_its_name_as_written()
    {
        var movement = Record(StockMovementReason.Received, 5);

        Assert.Equal(Store.Id, movement.LocationId);
        Assert.Equal("IT Store Room", movement.Location);
    }

    [Fact]
    public void Ticket_numbers_are_stored_upper_case_like_other_tickets()
    {
        var movement = StockMovement.Record(Bag, Store, StockMovementReason.Issued, 1, 5, "tester", serviceDeskRequestId: " inc70001 ");

        Assert.Equal("INC70001", movement.ServiceDeskRequestId);
    }

    [Fact]
    public void Closed_location_can_be_emptied_but_not_restocked()
    {
        var closed = new Locations.Location { Name = "Old Sydney Office", IsActive = false };

        Assert.Throws<DomainException>(() => StockMovement.Record(Bag, closed, StockMovementReason.Received, 5, 0, "tester"));
        Assert.Equal(-3, StockMovement.Record(Bag, closed, StockMovementReason.Issued, 3, 3, "tester").Quantity);
    }

    [Theory]
    [InlineData(StockMovementReason.Received, 5, 5)]
    [InlineData(StockMovementReason.Returned, 1, 1)]
    [InlineData(StockMovementReason.Issued, 3, -3)]
    [InlineData(StockMovementReason.WrittenOff, 2, -2)]
    public void Reason_decides_direction(StockMovementReason reason, int entered, int expected)
    {
        Assert.Equal(expected, Record(reason, entered).Quantity);
    }

    [Fact]
    public void Adjustment_is_signed_and_needs_a_note()
    {
        Assert.Equal(-4, Record(StockMovementReason.Adjustment, -4, note: "Stocktake count 16").Quantity);
        Assert.Throws<DomainException>(() => Record(StockMovementReason.Adjustment, -4));
    }

    [Fact]
    public void Cannot_issue_more_than_on_hand()
    {
        var error = Assert.Throws<DomainException>(() => Record(StockMovementReason.Issued, 5, current: 3));
        Assert.Contains("Only 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Negative_entry_is_rejected_for_non_adjustments()
    {
        Assert.Throws<DomainException>(() => Record(StockMovementReason.Issued, -5));
        Assert.Throws<DomainException>(() => Record(StockMovementReason.Received, 0));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(10, true)]
    [InlineData(0, true)]
    public void Low_stock_is_at_or_below_reorder_level(int onHand, bool low)
    {
        Assert.Equal(low, Bag.IsLow(onHand));
    }
}
