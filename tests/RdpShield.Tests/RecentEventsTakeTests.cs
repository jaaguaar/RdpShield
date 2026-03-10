using RdpShield.Api;

namespace RdpShield.Tests.Api;

public class RecentEventsTakeTests
{
    [Theory]
    [InlineData(0, RecentEventsTake.Default)]
    [InlineData(-1, RecentEventsTake.Default)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 500)]
    [InlineData(501, RecentEventsTake.Max)]
    [InlineData(10000, RecentEventsTake.Max)]
    public void Normalize_clamps_requested_take(int requested, int expected)
    {
        Assert.Equal(expected, RecentEventsTake.Normalize(requested));
    }
}
