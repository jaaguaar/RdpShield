using RdpShield.Core.Abstractions;

namespace RdpShield.Infrastructure.Sqlite;

public sealed class StatsServiceSqlite : IStatsService
{
    private readonly SqliteDb _db;

    public StatsServiceSqlite(SqliteDb db) => _db = db;

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();

        var active = await ExecuteScalarLongAsync(conn, "SELECT COUNT(1) FROM bans WHERE active = 1;", ct);

        var since = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");
        var failed10m = await ExecuteScalarLongAsync(
            conn,
            "SELECT COUNT(1) FROM events WHERE type = 'AuthFailedDetected' AND ts_utc >= @since;",
            ct,
            ("@since", since));

        string? lastIp = null;
        DateTimeOffset? lastAt = null;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ip, ts_utc
FROM events
WHERE type = 'IpBanned'
ORDER BY id DESC
LIMIT 1;";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                lastIp = reader.IsDBNull(0) ? null : reader.GetString(0);
                var ts = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(ts))
                    lastAt = DateTimeOffset.Parse(ts);
            }
        }

        return new DashboardStats(
            ActiveBansCount: (int)active,
            FailedAttemptsLast10m: (int)failed10m,
            LastBannedIp: lastIp,
            LastBannedAtUtc: lastAt);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : Convert.ToInt64(result ?? 0);
    }
}
