using System.Text.Json;

namespace RdpShield.Service.Settings;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();

    private RuntimeSettings _current = new();

    public SettingsStore(string path)
    {
        _path = path;
    }

    public RuntimeSettings Current
    {
        get
        {
            lock (_lock)
                return Clone(_current);
        }
    }

    public void LoadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                _current = Normalize(new RuntimeSettings());
                SaveInternal(_current);
                return;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize(json, RdpShieldJsonContext.Default.RuntimeSettings) ?? new RuntimeSettings();
            _current = Normalize(loaded);

            // ensure file has normalized shape
            SaveInternal(_current);
        }
    }

    public void Update(RuntimeSettings newSettings)
    {
        lock (_lock)
        {
            _current = Normalize(newSettings);
            SaveInternal(_current);
        }
    }

    private void SaveInternal(RuntimeSettings s)
    {
        var json = JsonSerializer.Serialize(s, RdpShieldJsonContext.Default.RuntimeSettings);
        File.WriteAllText(_path, json);
    }

    private static RuntimeSettings Normalize(RuntimeSettings s)
    {
        if (s.SchemaVersion <= 0) s.SchemaVersion = 1;

        if (s.AttemptsThreshold < 1) s.AttemptsThreshold = 1;
        if (s.AttemptsThreshold > 50) s.AttemptsThreshold = 50;

        if (s.WindowSeconds < 10) s.WindowSeconds = 10;
        if (s.WindowSeconds > 3600) s.WindowSeconds = 3600;

        if (s.BanMinutes < 1) s.BanMinutes = 1;
        if (s.BanMinutes > 7 * 24 * 60) s.BanMinutes = 7 * 24 * 60;

        if (s.AllowlistRefreshSeconds < 1) s.AllowlistRefreshSeconds = 1;
        if (s.AllowlistRefreshSeconds > 3600) s.AllowlistRefreshSeconds = 3600;

        if (string.IsNullOrWhiteSpace(s.FirewallRulePrefix))
            s.FirewallRulePrefix = "RdpShield Block";

        if (s.RdpPort < 1 || s.RdpPort > 65535) s.RdpPort = 3389;

        return s;
    }

    private static RuntimeSettings Clone(RuntimeSettings s) => new()
    {
        SchemaVersion = s.SchemaVersion,
        AttemptsThreshold = s.AttemptsThreshold,
        WindowSeconds = s.WindowSeconds,
        BanMinutes = s.BanMinutes,
        EnableFirewall = s.EnableFirewall,
        FirewallRulePrefix = s.FirewallRulePrefix,
        RdpPort = s.RdpPort,
        AllowlistRefreshSeconds = s.AllowlistRefreshSeconds
    };
}
