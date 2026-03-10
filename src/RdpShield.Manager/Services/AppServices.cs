using RdpShield.Api.Client;

namespace RdpShield.Manager.Services;

public static class AppServices
{
    public static EventStreamService Events { get; } = new();
    public static ServiceConnectionState Connection { get; } =
        new ServiceConnectionState(RdpShieldClientFactory.Create(), TimeSpan.FromSeconds(3));
}