using Microsoft.Data.Sqlite;
using RdpShield.Core.Abstractions;
using RdpShield.Core.Models;

namespace RdpShield.Infrastructure.Sqlite;

public sealed class AllowlistStore : IAllowlistStore
{
    private readonly SqliteDb _db;

    public AllowlistStore(SqliteDb db) => _db = db;

    public async Task<bool> IsAllowedAsync(string ip, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM allowlist WHERE ip_or_cidr = @ip;";
        cmd.Parameters.AddWithValue("@ip", ip);

        var countObj = await cmd.ExecuteScalarAsync(ct);
        var count = countObj is long l ? l : Convert.ToInt64(countObj ?? 0);
        return count > 0;
    }

    public async Task<IReadOnlyList<AllowlistItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
  ip_or_cidr AS Entry,
  comment    AS Comment
FROM allowlist
ORDER BY ip_or_cidr;";

        var rows = new List<AllowlistItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AllowlistItem(
                Entry: reader.GetString(0),
                Comment: reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    public async Task AddOrUpdateAsync(string ipOrCidr, string? comment, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO allowlist(ip_or_cidr, comment, created_at_utc)
VALUES(@ip, @comment, @ts)
ON CONFLICT(ip_or_cidr) DO UPDATE SET comment = excluded.comment;";
        cmd.Parameters.AddWithValue("@ip", ipOrCidr);
        cmd.Parameters.AddWithValue("@comment", (object?)comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(string ipOrCidr, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM allowlist WHERE ip_or_cidr = @ip;";
        cmd.Parameters.AddWithValue("@ip", ipOrCidr);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
