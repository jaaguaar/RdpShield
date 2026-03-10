using System.Net;
using System.Numerics;

namespace RdpShield.Core.Security;

public static class CidrMatcher
{
    // Supports:
    // - single IP: "192.168.1.10" / "2001:db8::1"
    // - CIDR: "192.168.1.0/24" / "2001:db8::/32"
    public static bool TryParse(string text, out CidrEntry entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();

        var slash = text.IndexOf('/');
        if (slash < 0)
        {
            if (!IPAddress.TryParse(text, out var ip)) return false;
            entry = CidrEntry.ForSingleIp(ip);
            return true;
        }

        var ipPart = text[..slash].Trim();
        var prefixPart = text[(slash + 1)..].Trim();

        if (!IPAddress.TryParse(ipPart, out var network)) return false;
        if (!int.TryParse(prefixPart, out var prefix)) return false;

        var bits = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < 0 || prefix > bits) return false;

        entry = CidrEntry.ForNetwork(network, prefix);
        return true;
    }

    public static bool Contains(in CidrEntry entry, IPAddress ip)
    {
        // Normalize IPv4-mapped IPv6
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (entry.IsV4 && ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        if (!entry.IsV4 && ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;

        var ipVal = ToBigInt(ip);
        var netVal = entry.NetworkBigInt;

        // mask bits: top prefix bits must match
        var shift = entry.TotalBits - entry.PrefixLength;
        return (ipVal >> shift) == (netVal >> shift);
    }

    private static BigInteger ToBigInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        // BigInteger expects little-endian; add 0 to avoid sign
        Array.Reverse(bytes);
        var unsigned = bytes.Concat(new byte[] { 0 }).ToArray();
        return new BigInteger(unsigned);
    }
}

public readonly record struct CidrEntry(bool IsV4, int TotalBits, int PrefixLength, BigInteger NetworkBigInt)
{
    public static CidrEntry ForSingleIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var isV4 = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        var bits = isV4 ? 32 : 128;
        return ForNetwork(ip, bits);
    }

    public static CidrEntry ForNetwork(IPAddress network, int prefix)
    {
        if (network.IsIPv4MappedToIPv6) network = network.MapToIPv4();
        var isV4 = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        var bits = isV4 ? 32 : 128;
        var netVal = ToBigInt(network);

        // Zero host bits (normalize network)
        var hostBits = bits - prefix;
        var normalized = (netVal >> hostBits) << hostBits;

        return new CidrEntry(isV4, bits, prefix, normalized);

        static BigInteger ToBigInt(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            Array.Reverse(bytes);
            var unsigned = bytes.Concat(new byte[] { 0 }).ToArray();
            return new BigInteger(unsigned);
        }
    }
}