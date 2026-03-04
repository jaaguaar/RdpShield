# RdpShield

RdpSentinel is a lightweight Windows security tool that helps protect Remote Desktop (RDP) from brute-force attacks.

It runs as a **Windows Service**, watches **Windows Security Event Log** for failed logons (e.g. **Event ID 4625**), and automatically **blocks abusive IP addresses** using **Windows Firewall**.  
A separate **GUI Manager** (planned) will allow you to start/stop the service and manage allowlists and bans.

## Features

- Detects repeated failed logons (Event ID 4625)
- Blocks attackers by adding inbound Windows Firewall rules for TCP/3389
- Allowlist support (never ban trusted IPs)
- Configurable thresholds and ban duration
- Designed to run as a Windows Service (Worker Service / .NET)

## Planned

- GUI Manager (WinForms/WPF):
  - Start/Stop/Restart service
  - Edit allowlist
  - View blocked IPs + Unban
  - Show recent events/logs
- State persistence (bans survive service restart)
- Progressive bans (1h → 6h → 24h)
- CIDR allowlist support

## Requirements

- Windows Server 2016/2019/2022 (or Windows 10/11 for testing)
- .NET 8 SDK (for building)
- Service account must have permissions to:
  - Read Security Event Log
  - Manage Windows Firewall rules

## Quick start (build)

From repo root:

```powershell
dotnet restore
dotnet build -c Release
