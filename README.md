# RdpShield

RdpShield is a Windows-first security service that protects Remote Desktop (RDP) from brute-force attacks.

It runs as a Windows Service, watches Security Log failed logons (Event ID 4625), detects abusive IPs, and blocks them via Windows Firewall.

The solution also includes:
- `RdpShield.Manager` (WinUI 3 desktop app)
- `RdpShield.Cli` (automation/debug CLI)
- local IPC API over named pipes
- SQLite persistence for bans, allowlist, and events

## What It Does

- Monitors failed RDP authentication attempts from Windows Security log
- Applies threshold/window policy (for example: 3 attempts in 120 seconds)
- Skips trusted addresses from allowlist (IP + CIDR)
- Creates firewall block rules for abusive IPs
- Stores state/events in SQLite
- Automatically unbans expired records and removes firewall rules
- Exposes management API via named pipes for Manager/CLI
- Streams live events to UI clients

## Project Structure

- `src/RdpShield.Service` - Windows Worker Service, pipe servers, settings, orchestration
- `src/RdpShield.Core` - domain models, engine, abstractions
- `src/RdpShield.Infrastructure.Windows` - EventLog monitor + Windows Firewall provider
- `src/RdpShield.Infrastructure.Sqlite` - SQLite stores and schema
- `src/RdpShield.Api` - shared DTO/contracts for pipe API
- `src/RdpShield.Api.Client` - typed client for pipes (used by Manager/CLI)
- `src/RdpShield.Manager` - WinUI 3 desktop manager
- `src/RdpShield.Cli` - command-line client
- `tests/RdpShield.Tests` - unit/integration-style tests for core and stores
- `scripts` - publish/install/uninstall scripts

## Runtime Data

Default runtime data path (service mode):
- `%ProgramData%\RdpShield`

Files:
- `rdpshield.db` - SQLite database (bans, allowlist, events)
- `settings.json` - runtime policy/settings used by the service

In Development environment, service uses local `.data` folder under current directory.

## Requirements

- Windows 10/11 or Windows Server 2016+
- .NET 10 SDK (build)
- Administrator privileges for install/uninstall scripts and firewall operations
- Service account permission to read Security log and manage firewall rules

## Build

From repository root:

```powershell
dotnet restore
dotnet build RdpShield.slnx -c Debug
```

## Run (Developer)

Run service as console app:

```powershell
dotnet run --project .\src\RdpShield.Service\RdpShield.Service.csproj
```

Run Manager:

```powershell
dotnet run --project .\src\RdpShield.Manager\RdpShield.Manager.csproj
```

CLI examples:

```powershell
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- stats
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- bans
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- events 50
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- unban 1.2.3.4
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- allow
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- allow-add 10.0.0.0/24 "Office network"
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- allow-del 10.0.0.0/24
dotnet run --project .\src\RdpShield.Cli\RdpShield.Cli.csproj -- tail
```

## Publish

```powershell
.\scripts\publish.ps1 -Configuration Release -Runtime win-x64
```

With explicit version (used for EXE/DLL file properties):

```powershell
.\scripts\publish.ps1 -Configuration Release -Runtime win-x64 -Version 1.2.3
```

Output:
- `dist/Service`
- `dist/Manager`

Notes:
- Service is published (`dotnet publish`) with publish profile.
- Manager is built (`dotnet build`) and copied from `bin` output because WinUI 3 packaging via plain `dotnet publish` can miss required XAML/runtime assets.

## Install as Windows Service

```powershell
.\scripts\install.ps1
```

This script:
- copies binaries to `%ProgramFiles%\RdpShield`
- creates/starts Windows Service `RdpShield`

Uninstall:

```powershell
.\scripts\uninstall.ps1
```

Optional data removal:

```powershell
.\scripts\uninstall.ps1 -RemoveData
```

## Configuration

### Runtime settings (`settings.json`)

Service reads/writes runtime policy in `settings.json`:
- `AttemptsThreshold`
- `WindowSeconds`
- `BanMinutes`
- `EnableFirewall`
- `FirewallRulePrefix`
- `AllowlistRefreshSeconds`

These settings can be changed from Manager or via pipe API (`GetSettings` / `UpdateSettings`) and are applied live.

### App settings (`appsettings.json`)

`appsettings.json` is used mainly for host/service-level configuration (logging and general host defaults). The active ban policy is sourced from `settings.json` at runtime.

## Security Notes

- Named pipes are restricted to local users/admins/system via ACL.
- Firewall operations use PowerShell `Get-NetFirewallRule` / `New-NetFirewallRule` / `Remove-NetFirewallRule`.
- If firewall action fails, event is logged and service continues (state remains in DB).

## Testing

```powershell
dotnet test .\tests\RdpShield.Tests\RdpShield.Tests.csproj -v minimal
```

## Current Status

Implemented and working:
- service monitoring + ban flow
- sqlite persistence
- allowlist (IP and CIDR)
- manager UI pages (dashboard/activity/bans/allowlist/settings)
- CLI tooling
- install/publish scripts

Potential future improvements:
- progressive ban strategy
- richer auth/ACL model for management endpoints
- remote management story (currently local-machine oriented)

## Installer (WiX + Custom Bootstrapper)

The repository includes a WiX installer stack in `installer/`:
- `installer/RdpShield.Msi` - MSI with Service/Manager payload
- `installer/RdpShield.Bundle` - Burn bundle (`setup.exe`) that installs prerequisites and then MSI
- `installer/RdpShield.Bootstrapper` - custom BA (WPF UI) for install/update/uninstall flow
- `installer/Installer.Version.props` - product/version/URLs (single place for release metadata)

Current chain:
- `.NET Desktop Runtime 10 x64` (online download)
- `Windows App Runtime 1.8 x64` (online download)
- `RdpShield` MSI

Build installer:

```powershell
.\scripts\build-installer.ps1 -Configuration Release -Runtime win-x64
```

With explicit version (MSI/Bundle version + app binaries):

```powershell
.\scripts\build-installer.ps1 -Configuration Release -Runtime win-x64 -Version 1.2.3
```

Optional:

```powershell
.\scripts\build-installer.ps1 -Configuration Release -Runtime win-x64 -IncludeWindowsAppRuntime:$false
```

Notes:
- Bootstrapper UI supports prerequisite checkboxes and shortcut options (desktop/start menu).
- Prerequisite installers are downloaded from URLs in `installer/Installer.Version.props` and embedded into setup payloads.
- Detect conditions are used to avoid reinstalling already-present prerequisites.
- URLs can be switched to internal mirrors/offline payloads for disconnected installs.

## Automated Versioning (GitHub Actions)

Workflow: `.github/workflows/release.yml`

Behavior:
- Trigger: push to `main` (or manual run).
- Reads the latest tag matching `vX.Y.Z`.
- Reads commit subjects since that tag and determines bump by prefix:
  - `[major]` -> major bump
  - `[minor]` -> minor bump
  - `[patch]` -> patch bump
- Builds/tests/publishes with computed version.
- Builds MSI + Setup EXE with the same version.
- Creates GitHub Release and tag `vX.Y.Z`.

Example commit messages:
- `[patch] fix dashboard footer text`
- `[minor] add quick actions IP block`
- `[major] switch API contract for events`
