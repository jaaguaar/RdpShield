using System.Xml.Linq;

namespace RdpShield.Core.Security;

public sealed record Event4625Data(
    string? RemoteIp,
    string? Username,
    string? LogonType)
{
    public bool IsRemoteDesktopFailure
        // RDP failures typically show up as:
        // - 10: classic RemoteInteractive
        // - 3: Network logon when NLA validates credentials before session creation
        => string.Equals(LogonType, "10", StringComparison.Ordinal) ||
           string.Equals(LogonType, "3", StringComparison.Ordinal);
}

public static class Event4625Parser
{
    public static Event4625Data Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new Event4625Data(null, null, null);

        try
        {
            var doc = XDocument.Parse(xml);
            return new Event4625Data(
                RemoteIp: ReadDataField(doc, "IpAddress"),
                Username: NormalizeUsername(ReadDataField(doc, "TargetUserName")),
                LogonType: ReadDataField(doc, "LogonType"));
        }
        catch
        {
            return new Event4625Data(null, null, null);
        }
    }

    private static string? ReadDataField(XDocument doc, string fieldName)
    {
        var data = doc
            .Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName.Equals("Data", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Attribute("Name")?.Value, fieldName, StringComparison.OrdinalIgnoreCase));

        var value = data?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username) || username == "-")
            return null;

        return username;
    }
}
