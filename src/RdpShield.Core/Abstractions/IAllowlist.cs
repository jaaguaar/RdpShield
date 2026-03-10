namespace RdpShield.Core.Abstractions;

public interface IAllowlist
{
    bool IsAllowed(string ip);
}