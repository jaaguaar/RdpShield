using System.Diagnostics;
using System.Runtime.Versioning;
using RdpShield.Core.Abstractions;

namespace RdpShield.Infrastructure.Windows.Firewall;

[SupportedOSPlatform("windows")]
public sealed class WindowsFirewallProvider : IFirewallProvider
{
    public Task<bool> IsBannedAsync(string ip, CancellationToken ct = default)
    {
        // Check by remote address filter (best-effort)
        var cmd = $@"
$ip = '{Escape(ip)}'
$rules = Get-NetFirewallRule -ErrorAction SilentlyContinue
foreach ($r in $rules) {{
  try {{
    $addr = Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $r -ErrorAction SilentlyContinue
    if ($null -ne $addr -and $addr.RemoteAddress -contains $ip) {{ exit 0 }}
  }} catch {{}}
}}
exit 1
";
        var exit = RunWinPS(cmd);
        return Task.FromResult(exit == 0);
    }

    public Task BanIpAsync(string ip, string ruleName, int port, string? description = null, CancellationToken ct = default)
    {
        if (!System.Net.IPAddress.TryParse(ip, out _))
            throw new ArgumentException($"Invalid IP address: '{ip}'.", nameof(ip));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");

        var cmd = $@"
$name = '{Escape(ruleName)}'
$ip   = '{Escape(ip)}'
$port = {port}
$desc = '{Escape(description ?? "Blocked by RdpShield")}'

Remove-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Block -Enabled True -Protocol TCP -LocalPort $port -RemoteAddress $ip -Profile Any -Description $desc -ErrorAction Stop | Out-Null
exit 0
";
        EnsureOk(RunWinPS(cmd), "BanIpAsync failed");
        return Task.CompletedTask;
    }

    public Task UnbanIpAsync(string ip, string ruleName, CancellationToken ct = default)
    {
        var cmd = $@"
$name = '{Escape(ruleName)}'

Remove-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Out-Null
exit 0
";
        EnsureOk(RunWinPS(cmd), "UnbanIpAsync failed");
        return Task.CompletedTask;
    }

    private static int RunWinPS(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe", // Windows PowerShell 5.1
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;
        p.WaitForExit();

        // You can log these later if needed:
        // var stdout = p.StandardOutput.ReadToEnd();
        // var stderr = p.StandardError.ReadToEnd();

        return p.ExitCode;
    }

    private static void EnsureOk(int exitCode, string message)
    {
        if (exitCode != 0)
            throw new InvalidOperationException($"{message}. ExitCode={exitCode}");
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
