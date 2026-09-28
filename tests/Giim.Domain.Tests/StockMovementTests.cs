using Giim.Domain.Common;
using Giim.Domain.Stock;

namespace Giim.Domain.Tests;

public class StockMovementTests
{
    private static readonly StockItem Bag = new() { Name = "Laptop bag", ReorderLevel = 10 };

    private static StockMovement Record(StockMovementReason reason, int quantity, int current = 20, string? note = null) =>
        StockMovement.Record(Bag, "IT Store Room", reason, quantity, current, "tester", note);

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
