namespace RdpShield.Core.Models;

public sealed record BanRecord(
    string Ip,
    string Reason,
    string Source,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset ExpiresUtc,
    int AttemptsInWindow
);