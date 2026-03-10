using RdpShield.Api;
using RdpShield.Core.Abstractions;
using System.Text.Json;

namespace RdpShield.Service.Live;

public sealed class LiveEventStoreDecorator : IEventStore
{
    private readonly IEventStore _inner;
    private readonly LiveEventHub _hub;

    public LiveEventStoreDecorator(IEventStore inner, LiveEventHub hub)
    {
        _inner = inner;
        _hub = hub;
    }

    public async Task AppendAsync(
        DateTimeOffset tsUtc,
        string level,
        string type,
        string message,
        string? ip = null,
        string? source = null,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        await _inner.AppendAsync(tsUtc, level, type, message, ip, source, payloadJson, ct);

        string? username = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
                    username = un.GetString();
            }
            catch
            {
                // Best effort; do not block live event fan-out.
            }
        }

        _hub.Publish(new EventDto
        {
            TsUtc = tsUtc,
            Level = level,
            Type = type,
            Message = message,
            Username = username,
            Ip = ip,
            Source = source
        });
    }

    public Task<IReadOnlyList<(DateTimeOffset tsUtc, string level, string type, string message, string? ip, string? source, string? payloadJson)>> GetLatestAsync(int take, CancellationToken ct = default, int skip = 0)
        => _inner.GetLatestAsync(take, ct, skip);
}
