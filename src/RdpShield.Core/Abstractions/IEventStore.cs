namespace RdpShield.Core.Abstractions;

public interface IEventStore
{
    Task AppendAsync(DateTimeOffset tsUtc, string level, string type, string message, string? ip = null, string? source = null, string? payloadJson = null, CancellationToken ct = default);
    Task<IReadOnlyList<(DateTimeOffset tsUtc, string level, string type, string message, string? ip, string? source, string? payloadJson)>> GetLatestAsync(int take, CancellationToken ct = default, int skip = 0);
}
