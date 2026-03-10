using RdpShield.Core.Models;

namespace RdpShield.Core.Abstractions;

public interface IMonitor
{
    IAsyncEnumerable<SecurityEvent> WatchAsync(CancellationToken ct = default);
}