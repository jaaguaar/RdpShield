namespace RdpShield.Core.Models;

public sealed record RdpShieldSettings(
    int ThresholdCount = 3,
    int ThresholdWindowSeconds = 120,
    int BanDurationMinutes = 60
);