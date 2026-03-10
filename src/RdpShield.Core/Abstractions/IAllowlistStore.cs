using RdpShield.Core.Models;

namespace RdpShield.Core.Abstractions;

public interface IAllowlistStore
{
    Task<bool> IsAllowedAsync(string ip, CancellationToken ct = default);

    Task<IReadOnlyList<AllowlistItem>> GetAllAsync(CancellationToken ct = default);

    Task AddOrUpdateAsync(string ipOrCidr, string? comment, CancellationToken ct = default);
    Task RemoveAsync(string ipOrCidr, CancellationToken ct = default);
}