namespace RdpShield.Service.Settings;

public sealed class RuntimeSettings
{
    public int SchemaVersion { get; set; } = 1;

    // Engine
    public int AttemptsThreshold { get; set; } = 3;
    public int WindowSeconds { get; set; } = 120;
    public int BanMinutes { get; set; } = 120;

    // Firewall
    public bool EnableFirewall { get; set; } = true;
    public string FirewallRulePrefix { get; set; } = "RdpShield Block";
    public int RdpPort { get; set; } = 3389;

    // Allowlist cache refresh
    public int AllowlistRefreshSeconds { get; set; } = 10;
}
