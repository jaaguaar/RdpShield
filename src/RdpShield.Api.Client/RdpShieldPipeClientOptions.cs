using RdpShield.Api;

namespace RdpShield.Api.Client;

public sealed class RdpShieldPipeClientOptions
{
    public string PipeName { get; set; } = PipeProtocol.BasePipeName;
    public int ConnectTimeoutMs { get; set; } = 10000;
    public int RequestTimeoutMs { get; set; } = 10000;
}
