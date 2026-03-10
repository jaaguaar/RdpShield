namespace RdpShield.Core.Abstractions;

public interface IFirewallProvider
{
    Task<bool> IsBannedAsync(string ip, CancellationToken ct = default);
    Task BanIpAsync(string ip, string ruleName, int port, string? description = null, CancellationToken ct = default);
    Task UnbanIpAsync(string ip, string ruleName, CancellationToken ct = default);
}