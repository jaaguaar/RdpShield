using RdpShield.Api.Client;

namespace RdpShield.Manager.Services;

public static class RdpShieldClientFactory
{
    public static IRdpShieldClient Create()
        => new RdpShieldPipeClient(new RdpShieldPipeClientOptions());
}
