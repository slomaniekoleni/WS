# Copies the API's dotnet user-secrets into .env (for Docker), without printing them.
# Usage: .\scripts\env-from-user-secrets.ps1 [-Force]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$envFile = Join-Path $root '.env'

if ((Test-Path $envFile) -and -not $Force) {
    Write-Host ".env already exists. Re-run with -Force to overwrite it." -ForegroundColor Yellow
    exit 1
}

$lines = dotnet user-secrets list --project (Join-Path $root 'src/Ws.Api')
if ($LASTEXITCODE -ne 0) { throw "dotnet user-secrets list failed" }

$out = @('# Generated from dotnet user-secrets by scripts/env-from-user-secrets.ps1. Never commit this file.')
$keys = @()
foreach ($line in $lines) {
    $i = $line.IndexOf(' = ')
    if ($i -lt 1) { continue }
    $key = $line.Substring(0, $i).Trim().Replace(':', '__')
    $value = $line.Substring($i + 3)
    # Single quotes keep the value literal in compose (no # comments or $ interpolation).
    if (-not $value.Contains("'")) { $value = "'$value'" }
    $out += "$key=$value"
    $keys += $key
}
if ($keys.Count -eq 0) { throw "No user-secrets found for src/Ws.Api" }

# UTF-8 without BOM: docker compose would read a BOM as part of the first key.
[System.IO.File]::WriteAllLines($envFile, $out, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Wrote .env with: $($keys -join ', ')"
foreach ($k in 'Claude__ApiKey', 'Telegram__BotToken', 'Admin__Login', 'Admin__Password') {
    if ($keys -notcontains $k) { Write-Host "  missing: $k" -ForegroundColor Yellow }
}
