namespace RdpShield.Core.Abstractions;

public sealed record DashboardStats(
    int ActiveBansCount,
    int FailedAttemptsLast10m,
    string? LastBannedIp,
    DateTimeOffset? LastBannedAtUtc
);

public interface IStatsService
{
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default);
}