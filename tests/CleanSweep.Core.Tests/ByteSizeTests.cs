using System.Globalization;
using CleanSweep.Core;

namespace CleanSweep.Core.Tests;

public sealed class ByteSizeTests
{
    public ByteSizeTests()
    {
        // Pin the culture so the decimal separator is deterministic across hosts.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1 TB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Human_formats_expected(long bytes, string expected)
        => Assert.Equal(expected, ByteSize.Human(bytes));

    [Fact]
    public void Human_rounds_to_two_decimals()
    {
        // 1234 B = 1.205078… KB -> "1.21 KB"
        Assert.Equal("1.21 KB", ByteSize.Human(1234));
    }

    [Fact]
    public void Human_clamps_negative_to_zero()
        => Assert.Equal("0 B", ByteSize.Human(-5));

    [Fact]
    public void Human_keeps_largest_unit_for_huge_values()
    {
        // Above PB the unit index is capped; value stays in PB.
        var text = ByteSize.Human(long.MaxValue);
        Assert.EndsWith(" PB", text);
    }
}
