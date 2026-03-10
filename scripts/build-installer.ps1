param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$Version = "",
  [bool]$IncludeWindowsAppRuntime = $true,
  [string]$DistDir = "$(Resolve-Path .)\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "== Build RdpShield Installer ==" -ForegroundColor Cyan

$env:DOTNET_CLI_HOME = Join-Path (Resolve-Path ".") ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

function Invoke-Dotnet {
  param([string[]]$DotnetArgs, [string]$ActionName)

  & dotnet @DotnetArgs
  if ($LASTEXITCODE -ne 0) {
    throw "$ActionName failed with exit code $LASTEXITCODE"
  }
}

function Download-IfMissing {
  param(
    [string]$Url,
    [string]$Path
  )

  if (Test-Path $Path) {
    return
  }

  $dir = Split-Path -Parent $Path
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Write-Host "Downloading $(Split-Path -Leaf $Path)..." -ForegroundColor Yellow
  Invoke-WebRequest -Uri $Url -OutFile $Path
}

if (-not (Test-Path (Join-Path $DistDir "Service"))) {
  Write-Host "dist not found, running publish first..." -ForegroundColor Yellow
  & "$PSScriptRoot\publish.ps1" -Configuration $Configuration -Runtime $Runtime -Version $Version -DistDir $DistDir
  if ($LASTEXITCODE -ne 0) {
    throw "publish.ps1 failed with exit code $LASTEXITCODE"
  }
}

$serviceDir = Join-Path $DistDir "Service"
$managerDir = Join-Path $DistDir "Manager"

if (-not (Test-Path (Join-Path $serviceDir "RdpShield.Service.exe"))) {
  throw "Service payload not found: $serviceDir"
}
if (-not (Test-Path (Join-Path $managerDir "RdpShield.Manager.exe"))) {
  throw "Manager payload not found: $managerDir"
}

$installerPropsPath = ".\installer\Installer.Version.props"
[xml]$installerProps = Get-Content $installerPropsPath

$dotnetUrl = $installerProps.Project.PropertyGroup.DotNetDesktopRuntimeUrl
$windowsAppRuntimeUrl = $installerProps.Project.PropertyGroup.WindowsAppRuntimeUrl

if ([string]::IsNullOrWhiteSpace($dotnetUrl) -or [string]::IsNullOrWhiteSpace($windowsAppRuntimeUrl)) {
  throw "Prerequisite URLs are missing in $installerPropsPath"
}

$effectiveVersion = $Version
if ([string]::IsNullOrWhiteSpace($effectiveVersion)) {
  $effectiveVersion = [string]$installerProps.Project.PropertyGroup.RdpShieldVersion
}
if ($effectiveVersion -notmatch '^\d+\.\d+\.\d+$') {
  throw "Installer version must match x.y.z (resolved value: '$effectiveVersion')."
}
Write-Host "Using installer version: $effectiveVersion" -ForegroundColor DarkCyan

$prereqDir = ".\installer\prereqs"
$dotnetInstallerPath = Resolve-Path (Join-Path $prereqDir "windowsdesktop-runtime-10-win-x64.exe") -ErrorAction SilentlyContinue
if ($dotnetInstallerPath) { $dotnetInstallerPath = $dotnetInstallerPath.Path }
if (-not $dotnetInstallerPath) { $dotnetInstallerPath = Join-Path (Resolve-Path ".") "installer\prereqs\windowsdesktop-runtime-10-win-x64.exe" }

$windowsAppRuntimeInstallerPath = Resolve-Path (Join-Path $prereqDir "windowsappruntimeinstall-x64.exe") -ErrorAction SilentlyContinue
if ($windowsAppRuntimeInstallerPath) { $windowsAppRuntimeInstallerPath = $windowsAppRuntimeInstallerPath.Path }
if (-not $windowsAppRuntimeInstallerPath) { $windowsAppRuntimeInstallerPath = Join-Path (Resolve-Path ".") "installer\prereqs\windowsappruntimeinstall-x64.exe" }

Download-IfMissing -Url $dotnetUrl -Path $dotnetInstallerPath
if ($IncludeWindowsAppRuntime) {
  Download-IfMissing -Url $windowsAppRuntimeUrl -Path $windowsAppRuntimeInstallerPath
}

Invoke-Dotnet -ActionName "MSI build" -DotnetArgs @(
  "build",
  ".\installer\RdpShield.Msi\RdpShield.Msi.wixproj",
  "-t:Rebuild",
  "-c", $Configuration,
  "-p:RdpShieldVersion=$effectiveVersion",
  "-p:ServiceSourceDir=$serviceDir",
  "-p:ManagerSourceDir=$managerDir"
)

$msi = Get-ChildItem ".\installer\RdpShield.Msi\bin" -Recurse -Filter "*.msi" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if (-not $msi) {
  throw "MSI output not found."
}

Invoke-Dotnet -ActionName "Bundle build" -DotnetArgs @(
  "build",
  ".\installer\RdpShield.Bundle\RdpShield.Bundle.wixproj",
  "-t:Rebuild",
  "-c", $Configuration,
  "-p:RdpShieldVersion=$effectiveVersion",
  "-p:MsiPath=$($msi.FullName)",
  "-p:IncludeWindowsAppRuntime=$([int]$IncludeWindowsAppRuntime)",
  "-p:DotNetDesktopRuntimePath=$dotnetInstallerPath",
  "-p:WindowsAppRuntimePath=$windowsAppRuntimeInstallerPath"
)

$bundle = Get-ChildItem ".\installer\RdpShield.Bundle\bin" -Recurse -Filter "*.exe" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if (-not $bundle) {
  throw "Bundle output not found."
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host "MSI:    $($msi.FullName)"
Write-Host "Setup:  $($bundle.FullName)"
