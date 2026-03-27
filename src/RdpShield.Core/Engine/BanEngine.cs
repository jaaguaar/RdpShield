using System.Collections.Concurrent;
using System.Net;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Models;

namespace RdpShield.Core.Engine;

public sealed class BanEngine
{
    private readonly IClock _clock;
    private readonly IAllowlist _allowlist;
    private readonly RdpShieldSettings _settings;

    // key: ip, value: timestamps of attempts (UTC)
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _attempts = new();

    public BanEngine(IClock clock, IAllowlist allowlist, RdpShieldSettings settings)
    {
        _clock = clock;
        _allowlist = allowlist;
        _settings = settings;
    }

    public BanDecision Process(SecurityEvent ev)
    {
        var ip = NormalizeIp(ev.RemoteIp);
        if (ip is null)
            return new BanDecision(false, null);

        if (_allowlist.IsAllowed(ip))
            return new BanDecision(false, null);

        var now = _clock.UtcNow;
        var windowStart = now.AddSeconds(-_settings.ThresholdWindowSeconds);

        var q = _attempts.GetOrAdd(ip, _ => new ConcurrentQueue<DateTimeOffset>());
        q.Enqueue(ev.TimestampUtc);

        // drop old
        while (q.TryPeek(out var t) && t < windowStart)
            q.TryDequeue(out _);

        var count = q.Count;

        if (count < _settings.ThresholdCount)
            return new BanDecision(false, null);

        // oldest remaining item in the FIFO queue is the actual first attempt in this window
        q.TryPeek(out var firstSeenUtc);

        var ban = new BanRecord(
            Ip: ip,
            Reason: $"Too many failed logons ({count}/{_settings.ThresholdCount}) in {_settings.ThresholdWindowSeconds}s",
            Source: ev.Source,
            FirstSeenUtc: firstSeenUtc,
            LastSeenUtc: now,
            ExpiresUtc: now.AddMinutes(_settings.BanDurationMinutes),
            AttemptsInWindow: count
        );

        // cleanup to avoid unbounded growth for "hot" IPs
        _attempts.TryRemove(ip, out _);

        return new BanDecision(true, ban);
    }

    private static string? NormalizeIp(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrWhiteSpace(raw) || raw == "-" || raw.Equals("::", StringComparison.OrdinalIgnoreCase))
            return null;

        // IPv6-mapped IPv4 like ::ffff:1.2.3.4
        if (IPAddress.TryParse(raw, out var addr))
        {
            if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
            return addr.ToString();
        }

        return null;
    }
}