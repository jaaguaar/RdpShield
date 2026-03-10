using RdpShield.Core.Models;

namespace RdpShield.Core.Engine;

public sealed record BanDecision(
    bool ShouldBan,
    BanRecord? BanRecord
);