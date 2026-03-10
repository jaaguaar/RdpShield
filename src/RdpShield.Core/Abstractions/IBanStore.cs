using RdpShield.Core.Models;

namespace RdpShield.Core.Abstractions;

public interface IBanStore
{
    Task UpsertBanAsync(BanRecord ban, CancellationToken ct = default);
    Task<IReadOnlyList<BanRecord>> GetActiveBansAsync(CancellationToken ct = default);
    Task<BanRecord?> GetBanAsync(string ip, CancellationToken ct = default);
    Task<bool> IsActiveBanAsync(string ip, CancellationToken ct = default);
    Task MarkUnbannedAsync(string ip, DateTimeOffset unbannedAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<BanRecord>> GetExpiredActiveBansAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}
