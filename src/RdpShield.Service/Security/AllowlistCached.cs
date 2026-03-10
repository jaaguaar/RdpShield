using RdpShield.Core.Abstractions;
using RdpShield.Core.Security;
using RdpShield.Service.Settings;

namespace RdpShield.Service.Security;

public sealed class AllowlistCached : IAllowlist
{
    private readonly IAllowlistStore _store;
    private readonly SettingsStore _settings;

    private readonly object _lock = new();
    private AllowlistMatcher _matcher = AllowlistMatcher.Build(Array.Empty<string>());
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;

    public AllowlistCached(IAllowlistStore store, SettingsStore settings)
    {
        _store = store;
        _settings = settings;
    }

    public bool IsAllowed(string ip)
    {
        EnsureFresh();
        lock (_lock)
            return _matcher.IsAllowed(ip);
    }

    private void EnsureFresh()
    {
        var now = DateTimeOffset.UtcNow;
        var refreshEvery = TimeSpan.FromSeconds(Math.Max(1, _settings.Current.AllowlistRefreshSeconds));

        if (now - _lastRefreshUtc < refreshEvery)
            return;

        lock (_lock)
        {
            refreshEvery = TimeSpan.FromSeconds(Math.Max(1, _settings.Current.AllowlistRefreshSeconds));
            if (now - _lastRefreshUtc < refreshEvery)
                return;

            // load allowlist from store
            var list = _store.GetAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            _matcher = AllowlistMatcher.Build(list.Select(x => x.Entry));
            _lastRefreshUtc = now;
        }
    }
}
