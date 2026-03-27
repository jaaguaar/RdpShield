using RdpShield.Core.Abstractions;
using RdpShield.Service.Settings;

namespace RdpShield.Service;

public sealed class BanCleanupService : BackgroundService
{
    private readonly ILogger<BanCleanupService> _logger;
    private readonly IBanStore _banStore;
    private readonly IEventStore _eventStore;
    private readonly IFirewallProvider _firewall;
    private readonly IClock _clock;
    private readonly SettingsStore _settings;

    public BanCleanupService(
        ILogger<BanCleanupService> logger,
        IBanStore banStore,
        IEventStore eventStore,
        IFirewallProvider firewall,
        IClock clock,
        SettingsStore settings)
    {
        _logger = logger;
        _banStore = banStore;
        _eventStore = eventStore;
        _firewall = firewall;
        _clock = clock;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _clock.UtcNow;

                var expired = await _banStore.GetExpiredActiveBansAsync(now, stoppingToken);

                foreach (var ban in expired)
                {
                    var s = _settings.Current;

                    if (s.EnableFirewall)
                    {
                        var ruleName = $"{s.FirewallRulePrefix} {ban.Ip}";
                        try
                        {
                            await _firewall.UnbanIpAsync(ban.Ip, ruleName, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            await _eventStore.AppendAsync(_clock.UtcNow, "Error", "FirewallError",
                                $"Failed to unban {ban.Ip} in firewall: {ex.Message}",
                                ip: ban.Ip, ct: stoppingToken);
                        }
                    }

                    await _banStore.MarkUnbannedAsync(ban.Ip, now, stoppingToken);

                    await _eventStore.AppendAsync(_clock.UtcNow, "Information", "IpUnbanned",
                        $"Unbanned {ban.Ip} (expired)", ip: ban.Ip, ct: stoppingToken);

                    _logger.LogInformation("Unbanned {Ip} (expired)", ban.Ip);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BanCleanupService loop failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}