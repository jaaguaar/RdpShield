using RdpShield.Core.Abstractions;
using RdpShield.Core.Engine;
using RdpShield.Service.Settings;

namespace RdpShield.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IMonitor _monitor;
    private readonly BanEngineFactory _engineFactory;
    private BanEngine _engine;

    private readonly IBanStore _banStore;
    private readonly IEventStore _eventStore;
    private readonly IClock _clock;
    private readonly IFirewallProvider _firewall;
    private readonly SettingsStore _settings;

    // track last engine settings to rebuild when changed
    private (int thr, int win, int ban) _engineKey;

    public Worker(
        ILogger<Worker> logger,
        IMonitor monitor,
        BanEngineFactory engineFactory,
        IBanStore banStore,
        IEventStore eventStore,
        IClock clock,
        IFirewallProvider firewall,
        SettingsStore settings)
    {
        _logger = logger;
        _monitor = monitor;
        _engineFactory = engineFactory;
        _engine = engineFactory.Create();

        _banStore = banStore;
        _eventStore = eventStore;
        _clock = clock;
        _firewall = firewall;
        _settings = settings;

        var s = _settings.Current;
        _engineKey = (s.AttemptsThreshold, s.WindowSeconds, s.BanMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RdpShield Service started.");
        await _eventStore.AppendAsync(_clock.UtcNow, "Information", "ServiceStarted", "RdpShield Service started", ct: stoppingToken);

        await foreach (var ev in _monitor.WatchAsync(stoppingToken))
        {
            RebuildEngineIfNeeded();

            await _eventStore.AppendAsync(
                _clock.UtcNow,
                "Information",
                "AuthFailedDetected",
                "Failed authentication attempt",
                ip: ev.RemoteIp,
                source: ev.Source,
                payloadJson: BuildAuthPayloadJson(ev.Username),
                ct: stoppingToken);

            var decision = _engine.Process(ev);

            if (!decision.ShouldBan || decision.BanRecord is null)
                continue;

            // prevent spam: if IP is already actively banned, do nothing
            var isActive = await _banStore.IsActiveBanAsync(decision.BanRecord.Ip, stoppingToken);
            if (isActive)
                continue;

            await _banStore.UpsertBanAsync(decision.BanRecord, stoppingToken);

            var s = _settings.Current;

            if (s.EnableFirewall)
            {
                try
                {
                    var ruleName = $"{s.FirewallRulePrefix} {decision.BanRecord.Ip}";
                    await _firewall.BanIpAsync(
                        decision.BanRecord.Ip,
                        ruleName,
                        3389,
                        $"RdpShield ban until {decision.BanRecord.ExpiresUtc:O}",
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add firewall rule for {Ip}", decision.BanRecord.Ip);

                    await _eventStore.AppendAsync(
                        _clock.UtcNow,
                        "Error",
                        "FirewallError",
                        $"Failed to ban {decision.BanRecord.Ip} in firewall: {ex.Message}",
                        ip: decision.BanRecord.Ip,
                        source: decision.BanRecord.Source,
                        ct: stoppingToken);
                    // continue work, DB ban still exists
                }
            }

            await _eventStore.AppendAsync(
                _clock.UtcNow,
                "Warning",
                "IpBanned",
                $"Banned {decision.BanRecord.Ip}: {decision.BanRecord.Reason}",
                ip: decision.BanRecord.Ip,
                source: decision.BanRecord.Source,
                ct: stoppingToken);

            _logger.LogWarning("Banned {Ip}. Reason: {Reason}", decision.BanRecord.Ip, decision.BanRecord.Reason);
        }
    }

    private void RebuildEngineIfNeeded()
    {
        var s = _settings.Current;
        var key = (s.AttemptsThreshold, s.WindowSeconds, s.BanMinutes);
        if (key == _engineKey)
            return;

        _engineKey = key;
        _engine = _engineFactory.Create();
        _logger.LogInformation("Ban engine rebuilt from settings.json (thr={Thr}, win={Win}, ban={Ban})", key.Item1, key.Item2, key.Item3);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RdpShield Service stopping.");
        await _eventStore.AppendAsync(_clock.UtcNow, "Information", "ServiceStopping", "RdpShield Service stopping", ct: cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private static string BuildAuthPayloadJson(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "{\"username\":null}";

        return $"{{\"username\":\"{EscapeJsonString(username)}\"}}";
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}
