using Microsoft.Data.Sqlite;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Models;

namespace RdpShield.Infrastructure.Sqlite;

public sealed class BanStore : IBanStore
{
    private readonly SqliteDb _db;

    public BanStore(SqliteDb db) => _db = db;

    public async Task UpsertBanAsync(BanRecord ban, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO bans(ip, reason, source, first_seen_utc, last_seen_utc, expires_utc, attempts_in_window, active, unbanned_at_utc)
VALUES(@Ip, @Reason, @Source, @FirstSeenUtc, @LastSeenUtc, @ExpiresUtc, @AttemptsInWindow, 1, NULL)
ON CONFLICT(ip) DO UPDATE SET
  reason = excluded.reason,
  source = excluded.source,
  first_seen_utc = excluded.first_seen_utc,
  last_seen_utc = excluded.last_seen_utc,
  expires_utc = excluded.expires_utc,
  attempts_in_window = excluded.attempts_in_window,
  active = 1,
  unbanned_at_utc = NULL;";
        cmd.Parameters.AddWithValue("@Ip", ban.Ip);
        cmd.Parameters.AddWithValue("@Reason", ban.Reason);
        cmd.Parameters.AddWithValue("@Source", ban.Source);
        cmd.Parameters.AddWithValue("@FirstSeenUtc", ban.FirstSeenUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@LastSeenUtc", ban.LastSeenUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@ExpiresUtc", ban.ExpiresUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@AttemptsInWindow", ban.AttemptsInWindow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<BanRecord>> GetActiveBansAsync(CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  ip,
  reason,
  source,
  first_seen_utc,
  last_seen_utc,
  expires_utc,
  attempts_in_window
FROM bans
WHERE active = 1
ORDER BY expires_utc;";

        var list = new List<BanRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadBanRecord(reader));

        return list;
    }

    public async Task<BanRecord?> GetBanAsync(string ip, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  ip,
  reason,
  source,
  first_seen_utc,
  last_seen_utc,
  expires_utc,
  attempts_in_window
FROM bans
WHERE ip = @ip;";
        cmd.Parameters.AddWithValue("@ip", ip);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadBanRecord(reader);
    }

    public async Task<bool> IsActiveBanAsync(string ip, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM bans WHERE ip = @ip AND active = 1;";
        cmd.Parameters.AddWithValue("@ip", ip);

        var countObj = await cmd.ExecuteScalarAsync(ct);
        var count = countObj is long l ? l : Convert.ToInt64(countObj ?? 0);
        return count > 0;
    }

    public async Task MarkUnbannedAsync(string ip, DateTimeOffset unbannedAtUtc, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE bans SET active = 0, unbanned_at_utc = @ts WHERE ip = @ip;";
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@ts", unbannedAtUtc.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<BanRecord>> GetExpiredActiveBansAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  ip,
  reason,
  source,
  first_seen_utc,
  last_seen_utc,
  expires_utc,
  attempts_in_window
FROM bans
WHERE active = 1 AND expires_utc <= @now
ORDER BY expires_utc;";
        cmd.Parameters.AddWithValue("@now", nowUtc.ToString("O"));

        var list = new List<BanRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadBanRecord(reader));

        return list;
    }

    private static BanRecord ReadBanRecord(SqliteDataReader r)
    {
        return new BanRecord(
            Ip: r.GetString(0),
            Reason: r.GetString(1),
            Source: r.GetString(2),
            FirstSeenUtc: DateTimeOffset.Parse(r.GetString(3)),
            LastSeenUtc: DateTimeOffset.Parse(r.GetString(4)),
            ExpiresUtc: DateTimeOffset.Parse(r.GetString(5)),
            AttemptsInWindow: r.GetInt32(6));
    }
}
