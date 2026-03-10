using System.Net;

namespace RdpShield.Core.Security;

public sealed class AllowlistMatcher
{
    private readonly HashSet<string> _exact;
    private readonly CidrEntry[] _ranges;

    private AllowlistMatcher(HashSet<string> exact, CidrEntry[] ranges)
    {
        _exact = exact;
        _ranges = ranges;
    }

    public static AllowlistMatcher Build(IEnumerable<string> entries)
    {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranges = new List<CidrEntry>();

        foreach (var raw in entries)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (CidrMatcher.TryParse(entry, out var cidr))
            {
                if (entry.Contains('/'))
                    ranges.Add(cidr);
                else
                    exact.Add(NormalizeIp(entry) ?? entry);
                continue;
            }

            exact.Add(entry);
        }

        return new AllowlistMatcher(exact, ranges.ToArray());
    }

    public bool IsAllowed(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;

        var normalized = NormalizeIp(ip) ?? ip.Trim();
        if (_exact.Contains(normalized))
            return true;

        if (!IPAddress.TryParse(normalized, out var addr))
            return false;

        foreach (var range in _ranges)
        {
            if (CidrMatcher.Contains(range, addr))
                return true;
        }

        return false;
    }

    private static string? NormalizeIp(string raw)
    {
        if (!IPAddress.TryParse(raw, out var ip))
            return null;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        return ip.ToString();
    }
}
