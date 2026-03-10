using NSubstitute;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Engine;
using RdpShield.Core.Models;
using Xunit;

namespace RdpShield.Tests.Engine;

public class BanEngineTests
{
    [Fact]
    public void Should_not_ban_if_ip_is_allowlisted()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-03-05T10:00:00Z"));

        var allowlist = Substitute.For<IAllowlist>();
        allowlist.IsAllowed("1.2.3.4").Returns(true);

        var settings = new RdpShieldSettings(ThresholdCount: 3, ThresholdWindowSeconds: 120, BanDurationMinutes: 60);
        var engine = new BanEngine(clock, allowlist, settings);

        for (var i = 0; i < 5; i++)
        {
            var ev = new SecurityEvent("RDP", "1.2.3.4", clock.UtcNow.AddSeconds(i));
            var decision = engine.Process(ev);
            Assert.False(decision.ShouldBan);
            Assert.Null(decision.BanRecord);
        }
    }

    [Fact]
    public void Should_ban_on_3rd_attempt_within_window()
    {
        var baseTime = DateTimeOffset.Parse("2026-03-05T10:00:00Z");

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(baseTime);

        var allowlist = Substitute.For<IAllowlist>();
        allowlist.IsAllowed(Arg.Any<string>()).Returns(false);

        var settings = new RdpShieldSettings(ThresholdCount: 3, ThresholdWindowSeconds: 120, BanDurationMinutes: 60);
        var engine = new BanEngine(clock, allowlist, settings);

        var d1 = engine.Process(new SecurityEvent("RDP", "5.6.7.8", baseTime.AddSeconds(0)));
        Assert.False(d1.ShouldBan);

        var d2 = engine.Process(new SecurityEvent("RDP", "5.6.7.8", baseTime.AddSeconds(10)));
        Assert.False(d2.ShouldBan);

        var d3 = engine.Process(new SecurityEvent("RDP", "5.6.7.8", baseTime.AddSeconds(20)));
        Assert.True(d3.ShouldBan);
        Assert.NotNull(d3.BanRecord);
        Assert.Equal("5.6.7.8", d3.BanRecord!.Ip);
        Assert.Equal(baseTime.AddMinutes(60), d3.BanRecord.ExpiresUtc);
    }

    [Fact]
    public void Should_not_count_attempts_outside_window()
    {
        var baseTime = DateTimeOffset.Parse("2026-03-05T10:00:00Z");

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(baseTime);

        var allowlist = Substitute.For<IAllowlist>();
        allowlist.IsAllowed(Arg.Any<string>()).Returns(false);

        var settings = new RdpShieldSettings(ThresholdCount: 3, ThresholdWindowSeconds: 30, BanDurationMinutes: 60);
        var engine = new BanEngine(clock, allowlist, settings);

        var d1 = engine.Process(new SecurityEvent("RDP", "9.9.9.9", baseTime.AddSeconds(-100)));
        Assert.False(d1.ShouldBan);

        var d2 = engine.Process(new SecurityEvent("RDP", "9.9.9.9", baseTime.AddSeconds(-90)));
        Assert.False(d2.ShouldBan);

        var d3 = engine.Process(new SecurityEvent("RDP", "9.9.9.9", baseTime.AddSeconds(-10)));
        Assert.False(d3.ShouldBan);

        var d4 = engine.Process(new SecurityEvent("RDP", "9.9.9.9", baseTime.AddSeconds(-5)));
        Assert.False(d4.ShouldBan);
    }
}