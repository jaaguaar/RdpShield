using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RdpShield.Service.Security;

public static class PipeSecurityFactory
{
    public static PipeSecurity Create()
    {
        var ps = new PipeSecurity();

        // Allow local users to connect (Manager runs as normal user)
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        ps.AddAccessRule(new PipeAccessRule(users, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // Allow admins full control
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        ps.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));

        // Allow LocalSystem (Windows Service account)
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        ps.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));

        return ps;
    }
}