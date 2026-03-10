using RdpShield.Core.Security;

namespace RdpShield.Tests.Core;

public class AllowlistMatcherTests
{
    [Fact]
    public void Allows_exact_ip_and_cidr_range()
    {
        var matcher = AllowlistMatcher.Build(new[]
        {
            "10.0.0.5",
            "192.168.10.0/24"
        });

        Assert.True(matcher.IsAllowed("10.0.0.5"));
        Assert.True(matcher.IsAllowed("192.168.10.77"));
        Assert.False(matcher.IsAllowed("192.168.11.1"));
    }

    [Fact]
    public void Supports_ipv6_mapped_ipv4_normalization()
    {
        var matcher = AllowlistMatcher.Build(new[] { "1.2.3.4" });
        Assert.True(matcher.IsAllowed("::ffff:1.2.3.4"));
    }
}
