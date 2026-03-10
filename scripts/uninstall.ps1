param(
  [string]$InstallRoot = "$env:ProgramFiles\RdpShield",
  [string]$ServiceName = "RdpShield",
  [string]$DataDir = "$env:ProgramData\RdpShield",
  [switch]$RemoveData
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

Write-Host "== Uninstall RdpShield ==" -ForegroundColor Cyan

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
  Write-Host "Stopping service..."
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  Write-Host "Deleting service..."
  sc.exe delete $ServiceName | Out-Null
  Wait-ServiceDeleted -Name $ServiceName -TimeoutSec 20
}

Write-Host "Removing install dir: $InstallRoot"
if (Test-Path $InstallRoot) {
  Remove-Item $InstallRoot -Recurse -Force
}

if ($RemoveData) {
  Write-Host "Removing data dir: $DataDir"
  if (Test-Path $DataDir) {
    Remove-Item $DataDir -Recurse -Force
  }
}

Write-Host "Done." -ForegroundColor Green
