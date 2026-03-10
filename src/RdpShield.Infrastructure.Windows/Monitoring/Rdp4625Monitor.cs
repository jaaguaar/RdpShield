using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Models;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace RdpShield.Infrastructure.Windows.Monitoring;

[SupportedOSPlatform("windows")]
public sealed class Rdp4625Monitor : IMonitor, IDisposable
{
    private readonly Channel<SecurityEvent> _channel = Channel.CreateUnbounded<SecurityEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly EventLogWatcher _watcher;

    public Rdp4625Monitor()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Rdp4625Monitor is supported only on Windows.");
        // Event ID 4625 in Security log
        var query = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4625)]]");
        _watcher = new EventLogWatcher(query);
        _watcher.EventRecordWritten += OnEventRecordWritten;
        _watcher.Enabled = true;
    }

    public async IAsyncEnumerable<SecurityEvent> WatchAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        // Reader loop
        while (await _channel.Reader.WaitToReadAsync(ct))
        {
            while (_channel.Reader.TryRead(out var ev))
                yield return ev;
        }
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            if (e.EventRecord is null) return;

            // Pull IP from event properties (best-effort)
            var ip = TryExtractIp(e.EventRecord);
            if (string.IsNullOrWhiteSpace(ip) || ip == "-" ) return;

            var ts = e.EventRecord.TimeCreated.HasValue
                ? new DateTimeOffset(e.EventRecord.TimeCreated.Value.ToUniversalTime())
                : DateTimeOffset.UtcNow;

            var username = TryExtractUsername(e.EventRecord);

            var sec = new SecurityEvent(
                Source: "RDP",
                RemoteIp: ip,
                TimestampUtc: ts,
                Username: username,
                Machine: Environment.MachineName,
                Raw: null);

            _channel.Writer.TryWrite(sec);
        }
        catch
        {
            // swallow any parsing issues to avoid killing the watcher thread
        }
    }

    private static string? TryExtractIp(EventRecord record)
    {
        // 4625 has "IpAddress" field, but indexes can vary depending on OS/language.
        // Try indexed properties first, then XML by Data Name.
        try
        {
            foreach (var p in record.Properties)
            {
                var s = p?.Value?.ToString();
                if (LooksLikeIp(s)) return s!;
            }
        }
        catch { /* ignore */ }

        var xmlValue = TryExtractDataField(record, "IpAddress");
        if (LooksLikeIp(xmlValue)) return xmlValue;

        return null;
    }

    private static string? TryExtractUsername(EventRecord record)
    {
        var username = TryExtractDataField(record, "TargetUserName");
        if (string.IsNullOrWhiteSpace(username) || username == "-")
            return null;

        return username;
    }

    private static string? TryExtractDataField(EventRecord record, string fieldName)
    {
        try
        {
            var doc = XDocument.Parse(record.ToXml());
            var data = doc
                .Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName.Equals("Data", StringComparison.OrdinalIgnoreCase) &&
                    e.Attribute("Name")?.Value.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true);

            return data?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeIp(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s == "-" || s.Equals("::", StringComparison.OrdinalIgnoreCase)) return false;

        // simple heuristic; normalization is handled later by BanEngine.NormalizeIp
        return s.Contains('.') || s.Contains(':');
    }

    public void Dispose()
    {
        try { _watcher.Enabled = false; } catch { }
        _watcher.EventRecordWritten -= OnEventRecordWritten;
        _watcher.Dispose();
        _channel.Writer.TryComplete();
    }
}
