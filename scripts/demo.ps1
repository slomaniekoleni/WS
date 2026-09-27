# Public demo from this PC: builds and starts the app in Docker behind a tunnel, picked from what's in .env:
#   TS_AUTHKEY   -> Tailscale Funnel, stable https://<TS_HOSTNAME>.<tailnet>.ts.net
#   TUNNEL_TOKEN -> Cloudflare named tunnel on your own domain (hostname set in the Cloudflare dashboard)
#   neither      -> Cloudflare quick tunnel, random https://*.trycloudflare.com (changes on restart)
# Stop with: docker compose --profile tailscale --profile named-tunnel --profile tunnel down
# Note: stop any `dotnet run` of the API first, only one process may poll the Telegram bot.
# Not 'Stop': Windows PowerShell 5.1 turns docker's progress output (stderr) into errors. Exit codes are checked instead.
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not (Test-Path '.env')) {
        Write-Host "No .env yet: run .\scripts\env-from-user-secrets.ps1 first (or copy .env.example)." -ForegroundColor Yellow
        exit 1
    }
    $envLines = Get-Content '.env'
    $has = { param($key) [bool]($envLines -match "^$key=\s*'?[^'\s]+") }
    $mode = if (& $has 'TS_AUTHKEY') { 'tailscale' } elseif (& $has 'TUNNEL_TOKEN') { 'named-tunnel' } else { 'tunnel' }
    Write-Host "Tunnel: $mode"

    # Only one tunnel at a time.
    foreach ($other in @('tailscale', 'named-tunnel', 'tunnel') | Where-Object { $_ -ne $mode }) {
        docker compose --profile $other stop $other *> $null
    }
    docker compose --profile $mode up -d --build
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed" }

    $url = $null
    Write-Host "Waiting for the public URL..."
    for ($i = 0; $i -lt 60 -and -not $url; $i++) {
        if ($mode -eq 'tailscale') {
            $json = docker compose --profile tailscale exec -T tailscale tailscale status --json 2>$null | Out-String
            if ($json -match '"DNSName":\s*"([^"]+?)\.?"') { $url = "https://$($Matches[1])" }
        }
        elseif ($mode -eq 'tunnel') {
            $logs = docker compose --profile tunnel logs tunnel 2>&1 | Out-String
            $m = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
            if ($m.Count -gt 0) { $url = $m[$m.Count - 1].Value }
        }
        else {
            Write-Host ""
            Write-Host "Named tunnel started: the site is on the hostname configured in the Cloudflare dashboard." -ForegroundColor Green
            Write-Host "Check the connection: docker compose --profile named-tunnel logs named-tunnel"
            exit 0
        }
        if (-not $url) { Start-Sleep -Seconds 2 }
    }
    if (-not $url) {
        Write-Host "No URL after 2 minutes; check: docker compose --profile $mode logs $mode" -ForegroundColor Yellow
        exit 1
    }
    Write-Host ""
    Write-Host "Site:  $url" -ForegroundColor Green
    Write-Host "Admin: $url/admin" -ForegroundColor Green
    if ($mode -eq 'tunnel') { Write-Host "(The URL changes every time the tunnel restarts.)" }
    if ($mode -eq 'tailscale') { Write-Host "(First visit after start can take a few seconds while the certificate is issued.)" }
}
finally { Pop-Location }
