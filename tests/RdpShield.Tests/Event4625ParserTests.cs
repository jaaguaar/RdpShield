using RdpShield.Core.Security;

namespace RdpShield.Tests.Core;

public class Event4625ParserTests
{
    [Fact]
    public void Parse_extracts_remote_desktop_failure_fields()
    {
        var xml = """
                  <Event>
                    <EventData>
                      <Data Name="TargetUserName">admin</Data>
                      <Data Name="LogonType">10</Data>
                      <Data Name="IpAddress">203.0.113.42</Data>
                    </EventData>
                  </Event>
                  """;

        var parsed = Event4625Parser.Parse(xml);

        Assert.True(parsed.IsRemoteDesktopFailure);
        Assert.Equal("admin", parsed.Username);
        Assert.Equal("203.0.113.42", parsed.RemoteIp);
    }

    [Fact]
    public void Parse_marks_nla_network_logon_as_remote_desktop_failure()
    {
        var xml = """
                  <Event>
                    <EventData>
                      <Data Name="TargetUserName">admin</Data>
                      <Data Name="LogonType">3</Data>
                      <Data Name="IpAddress">203.0.113.42</Data>
                    </EventData>
                  </Event>
                  """;

        var parsed = Event4625Parser.Parse(xml);

        Assert.True(parsed.IsRemoteDesktopFailure);
        Assert.Equal("203.0.113.42", parsed.RemoteIp);
    }

    [Fact]
    public void Parse_does_not_mark_non_rdp_logon_type_as_remote_desktop()
    {
        var xml = """
                  <Event>
                    <EventData>
                      <Data Name="TargetUserName">admin</Data>
                      <Data Name="LogonType">5</Data>
                      <Data Name="IpAddress">203.0.113.42</Data>
                    </EventData>
                  </Event>
                  """;

        var parsed = Event4625Parser.Parse(xml);

        Assert.False(parsed.IsRemoteDesktopFailure);
        Assert.Equal("203.0.113.42", parsed.RemoteIp);
    }
}
