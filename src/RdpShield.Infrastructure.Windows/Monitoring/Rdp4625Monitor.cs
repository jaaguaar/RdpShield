using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Models;
using RdpShield.Core.Security;
using System.Runtime.Versioning;

namespace RdpShield.Infrastructure.Windows.Monitoring;

[SupportedOSPlatform("windows")]
public sealed class Rdp4625Monitor : IMonitor, IDisposable
{
    private readonly ILogger<Rdp4625Monitor> _logger;
    private readonly Channel<SecurityEvent> _channel = Channel.CreateUnbounded<SecurityEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly EventLogWatcher _watcher;

    public Rdp4625Monitor(ILogger<Rdp4625Monitor> logger)
    {
        _logger = logger;
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

            var parsed = Event4625Parser.Parse(e.EventRecord.ToXml());
            if (!parsed.IsRemoteDesktopFailure)
                return;

            var ip = parsed.RemoteIp;
            if (string.IsNullOrWhiteSpace(ip) || ip == "-" ) return;
            if (!LooksLikeIp(ip)) return;

            var ts = e.EventRecord.TimeCreated.HasValue
                ? new DateTimeOffset(e.EventRecord.TimeCreated.Value.ToUniversalTime())
                : DateTimeOffset.UtcNow;

            var sec = new SecurityEvent(
                Source: "RDP",
                RemoteIp: ip,
                TimestampUtc: ts,
                Username: parsed.Username,
                Machine: Environment.MachineName,
                Raw: null);

            _channel.Writer.TryWrite(sec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Security event record; skipping.");
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
