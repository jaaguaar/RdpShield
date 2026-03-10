using RdpShield.Api;

namespace RdpShield.Api.Client;

public interface IRdpShieldClient
{
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BanDto>> GetActiveBansAsync(CancellationToken ct = default, int take = 200, int skip = 0);
    Task<IReadOnlyList<AllowlistDto>> GetAllowlistAsync(CancellationToken ct = default, int take = 200, int skip = 0);
    Task AddAllowlistEntryAsync(string entry, string? comment = null, CancellationToken ct = default);
    Task RemoveAllowlistEntryAsync(string entry, CancellationToken ct = default);
    Task BlockIpAsync(string ip, string? reason = null, CancellationToken ct = default);
    Task UnbanIpAsync(string ip, CancellationToken ct = default);
    Task<IReadOnlyList<EventDto>> GetRecentEventsAsync(int take = 20, CancellationToken ct = default, int skip = 0);
    Task<SettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(SettingsDto settings, CancellationToken ct = default);
}
