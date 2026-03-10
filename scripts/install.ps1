param(
  [string]$DistDir = "$(Resolve-Path .)\dist",
  [string]$InstallRoot = "$env:ProgramFiles\RdpShield",
  [string]$ServiceName = "RdpShield",
  [string]$DisplayName = "RdpShield Service",
  [string]$DataDir = "$env:ProgramData\RdpShield"
)

$ErrorActionPreference = "Stop"

function Require-Admin {
  $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
  if (-not $isAdmin) { throw "Run this script as Administrator." }
}

function Wait-ServiceDeleted {
  param(
    [string]$Name,
    [int]$TimeoutSec = 15
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) { return }
    Start-Sleep -Milliseconds 500
  }

  throw "Service '$Name' was not deleted within $TimeoutSec seconds."
}

Require-Admin

$svcSrc = Join-Path $DistDir "Service"
$mgrSrc = Join-Path $DistDir "Manager"
if (-not (Test-Path $svcSrc)) { throw "Service dist not found: $svcSrc. Run scripts\publish.ps1 first." }
if (-not (Test-Path $mgrSrc)) { throw "Manager dist not found: $mgrSrc. Run scripts\publish.ps1 first." }

$svcDst = Join-Path $InstallRoot "Service"
$mgrDst = Join-Path $InstallRoot "Manager"

Write-Host "== Install RdpShield ==" -ForegroundColor Cyan
Write-Host "InstallRoot: $InstallRoot"
Write-Host "DataDir:     $DataDir"

# Stop existing service if present
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Service exists. Stopping..."
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  Write-Host "Deleting existing service..."
  sc.exe delete $ServiceName | Out-Null
  Wait-ServiceDeleted -Name $ServiceName -TimeoutSec 20
}

# Create dirs
New-Item -ItemType Directory -Path $svcDst -Force | Out-Null
New-Item -ItemType Directory -Path $mgrDst -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null

# Clean destination folders to avoid stale DLL/resource conflicts between releases
Get-ChildItem -Path $svcDst -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $mgrDst -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Copy files with robust directory handling
Write-Host "Copying Service files..."
& robocopy $svcSrc $svcDst /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "Failed to copy service files. robocopy exit code: $rc" }

Write-Host "Copying Manager files..."
& robocopy $mgrSrc $mgrDst /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "Failed to copy manager files. robocopy exit code: $rc" }

# Determine service exe name (single-file publish still uses project assembly name)
$svcExe = Join-Path $svcDst "RdpShield.Service.exe"
if (-not (Test-Path $svcExe)) {
  # fallback: pick first exe in folder
  $svcExe = (Get-ChildItem $svcDst -Filter *.exe | Select-Object -First 1).FullName
}
if (-not (Test-Path $svcExe)) { throw "Service exe not found in $svcDst" }

# Create service (LocalSystem)
Write-Host "Creating Windows service..."
sc.exe create $ServiceName binPath= "`"$svcExe`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null

# Start service
Write-Host "Starting service..."
sc.exe start $ServiceName | Out-Null

Start-Sleep -Seconds 2
$svcState = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $svcState -or $svcState.Status -ne 'Running') {
  throw "Service '$ServiceName' failed to reach Running state."
}

Write-Host "`nInstalled." -ForegroundColor Green
Write-Host "Service: $ServiceName"
Write-Host "Manager: $mgrDst\RdpShield.Manager.exe"
Write-Host "DataDir: $DataDir"
Write-Host "Logs: $svcDst\logs"
