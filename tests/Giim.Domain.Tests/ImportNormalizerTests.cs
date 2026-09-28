using Giim.Domain.Assets;
using Giim.Domain.Importing;

namespace Giim.Domain.Tests;

public class ImportNormalizerTests
{
    [Theory]
    [InlineData(" 5cg1234xyz ", "5CG1234XYZ")]
    [InlineData("5CG 1234 XYZ", "5CG1234XYZ")]
    [InlineData("CN-0ABC-123", "CN0ABC123")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Serial_is_trimmed_upper_cased_and_stripped(string? raw, string? expected)
    {
        Assert.Equal(expected, ImportNormalizer.Serial(raw));
    }

    [Theory]
    [InlineData("Hewlett-Packard", "HP")]
    [InlineData("hp inc.", "HP")]
    [InlineData(" DELL ", "Dell")]
    [InlineData("Framework", "Framework")]
    public void Manufacturer_aliases_are_unified(string raw, string expected)
    {
        Assert.Equal(expected, ImportNormalizer.Manufacturer(raw));
    }

    [Theory]
    [InlineData("2024-03-07", 2024, 3, 7)]
    [InlineData("7/03/24", 2024, 3, 7)]
    [InlineData("07/03/2024", 2024, 3, 7)]
    [InlineData("7 Mar 2024", 2024, 3, 7)]
    public void Dates_are_read_day_first(string raw, int y, int m, int d)
    {
        Assert.True(ImportNormalizer.TryDate(raw, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Fact]
    public void Unreadable_date_is_reported()
    {
        Assert.False(ImportNormalizer.TryDate("sometime last year", out _));
    }

    [Theory]
    [InlineData("In Use", AssetStatus.Assigned)]
    [InlineData("Spare", AssetStatus.ReadyToDeploy)]
    [InlineData("RMA", AssetStatus.InRepair)]
    [InlineData("written off", AssetStatus.Disposed)]
    public void Legacy_statuses_map_onto_the_lifecycle(string raw, AssetStatus expected)
    {
        Assert.Equal(expected, ImportNormalizer.Status(raw));
    }
}
