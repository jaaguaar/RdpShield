namespace RdpShield.Core.Models;

public sealed record SecurityEvent(
    string Source,
    string RemoteIp,
    DateTimeOffset TimestampUtc,
    string? Username = null,
    string? Machine = null,
    string? Raw = null
);