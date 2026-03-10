using RdpShield.Core.Abstractions;

namespace RdpShield.Infrastructure.Sqlite;

public sealed class EventStore : IEventStore
{
    private readonly SqliteDb _db;

    public EventStore(SqliteDb db) => _db = db;

    public async Task AppendAsync(DateTimeOffset tsUtc, string level, string type, string message, string? ip = null, string? source = null, string? payloadJson = null, CancellationToken ct = default)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO events(ts_utc, level, type, message, ip, source, payload_json)
VALUES(@ts, @level, @type, @msg, @ip, @source, @payload);";
        cmd.Parameters.AddWithValue("@ts", tsUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@msg", message);
        cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@payload", (object?)payloadJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(DateTimeOffset tsUtc, string level, string type, string message, string? ip, string? source, string? payloadJson)>> GetLatestAsync(int take, CancellationToken ct = default, int skip = 0)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ts_utc, level, type, message, ip, source, payload_json
FROM events
ORDER BY id DESC
LIMIT @take OFFSET @skip;";
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@skip", Math.Max(0, skip));

        var list = new List<(DateTimeOffset tsUtc, string level, string type, string message, string? ip, string? source, string? payloadJson)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add((
                tsUtc: DateTimeOffset.Parse(reader.GetString(0)),
                level: reader.GetString(1),
                type: reader.GetString(2),
                message: reader.GetString(3),
                ip: reader.IsDBNull(4) ? null : reader.GetString(4),
                source: reader.IsDBNull(5) ? null : reader.GetString(5),
                payloadJson: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return list;
    }
}
