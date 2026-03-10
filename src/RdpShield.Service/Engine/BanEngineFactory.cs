using RdpShield.Core.Abstractions;
using RdpShield.Core.Engine;
using RdpShield.Service.Security;
using RdpShield.Service.Settings;

namespace RdpShield.Service;

public sealed class BanEngineFactory
{
    private readonly IClock _clock;
    private readonly IAllowlist _allowlist;
    private readonly SettingsStore _settings;

    public BanEngineFactory(IClock clock, IAllowlist allowlist, SettingsStore settings)
    {
        _clock = clock;
        _allowlist = allowlist;
        _settings = settings;
    }

    public BanEngine Create()
    {
        var s = _settings.Current;

        var coreSettings = new RdpShield.Core.Models.RdpShieldSettings(
            ThresholdCount: s.AttemptsThreshold,
            ThresholdWindowSeconds: s.WindowSeconds,
            BanDurationMinutes: s.BanMinutes
        );

        return new BanEngine(_clock, _allowlist, coreSettings);
    }
}