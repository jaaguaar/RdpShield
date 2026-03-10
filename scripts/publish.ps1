param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$Version = "",
  [bool]$ManagerSelfContained = $false,
  [string]$ServicePublishProfile = ".\src\RdpShield.Service\Properties\PublishProfiles\Service.Release.pubxml",
  [string]$DistDir = "$(Resolve-Path .)\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "== Publish RdpShield ==" -ForegroundColor Cyan

# Keep dotnet first-use data inside repo to avoid profile permission issues in CI/sandbox.
$env:DOTNET_CLI_HOME = Join-Path (Resolve-Path ".") ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

function Invoke-DotnetPublish {
  param([string[]]$DotnetArgs)

  & dotnet @DotnetArgs
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
  }
}

function Invoke-DotnetBuild {
  param([string[]]$DotnetArgs)

  & dotnet @DotnetArgs
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
  }
}

$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
  if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must match x.y.z (for example: 1.2.3)."
  }

  $fileVersion = "$Version.0"
  $versionArgs = @(
    "/p:Version=$Version",
    "/p:AssemblyVersion=$fileVersion",
    "/p:FileVersion=$fileVersion",
    "/p:InformationalVersion=$Version"
  )

  Write-Host "Using version: $Version (file/assembly: $fileVersion)" -ForegroundColor DarkCyan
}

if (Test-Path $DistDir) {
  Remove-Item $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $DistDir | Out-Null

# -------------------------
# Service (single file)
# -------------------------

$svcOut = Join-Path $DistDir "Service"

Invoke-DotnetPublish -DotnetArgs @(
  "publish",
  ".\src\RdpShield.Service\RdpShield.Service.csproj",
  "-c", $Configuration,
  "-r", $Runtime,
  "/p:PublishProfile=$ServicePublishProfile",
  $versionArgs,
  "-o", $svcOut
)

# -------------------------
# Manager (WinUI: self-contained build output is stable; publish drops XAML resources)
# -------------------------

$mgrOut = Join-Path $DistDir "Manager"
$managerSelfContainedArg = if ($ManagerSelfContained) { "true" } else { "false" }

Invoke-DotnetBuild -DotnetArgs @(
  "build",
  ".\src\RdpShield.Manager\RdpShield.Manager.csproj",
  "-c", $Configuration,
  "-r", $Runtime,
  "--self-contained", $managerSelfContainedArg,
  "/p:PublishReadyToRun=false",
  "/p:PublishTrimmed=false",
  "/p:DebugType=None",
  "/p:DebugSymbols=false",
  $versionArgs
)

$managerExe = Get-ChildItem -Path ".\src\RdpShield.Manager\bin\$Configuration" -Recurse -Filter "RdpShield.Manager.exe" |
  Where-Object { $_.FullName -match "\\$([Regex]::Escape($Runtime))\\" } |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if (-not $managerExe) {
  throw "Manager build output not found for runtime $Runtime."
}

$managerOutDir = Split-Path -Parent $managerExe.FullName
& robocopy $managerOutDir $mgrOut /S /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XD publish /XF *.pdb | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) {
  throw "robocopy failed with exit code $rc"
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host "Service: $svcOut"
Write-Host "Manager: $mgrOut"
