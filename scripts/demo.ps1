# Public demo from this PC: builds and starts the app in Docker behind a Cloudflare tunnel.
#   TUNNEL_TOKEN in .env -> named tunnel on your own domain (stable URL, set up in the Cloudflare dashboard)
#   no token             -> quick tunnel, prints a random https://*.trycloudflare.com URL (changes on restart)
# Stop with: docker compose --profile tunnel --profile named-tunnel down
# Note: stop any `dotnet run` of the API first, only one process may poll the Telegram bot.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not (Test-Path '.env')) {
        Write-Host "No .env yet: run .\scripts\env-from-user-secrets.ps1 first (or copy .env.example)." -ForegroundColor Yellow
        exit 1
    }
    $named = (Get-Content '.env') -match "^TUNNEL_TOKEN=\s*'?[^'\s]+"

    if ($named) {
        # Don't leave the old quick tunnel running next to the named one.
        docker compose --profile tunnel stop tunnel 2>$null | Out-Null
        docker compose --profile named-tunnel up -d --build
        if ($LASTEXITCODE -ne 0) { throw "docker compose failed" }
        Write-Host ""
        Write-Host "Named tunnel started: the site is on the hostname configured in the Cloudflare dashboard." -ForegroundColor Green
        Write-Host "Check the connection: docker compose --profile named-tunnel logs named-tunnel"
        exit 0
    }

    docker compose --profile tunnel up -d --build
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed" }

    Write-Host "Waiting for the tunnel URL..."
    for ($i = 0; $i -lt 60; $i++) {
        $logs = docker compose --profile tunnel logs tunnel 2>&1 | Out-String
        $m = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
        if ($m.Count -gt 0) {
            $url = $m[$m.Count - 1].Value
            Write-Host ""
            Write-Host "Site:  $url" -ForegroundColor Green
            Write-Host "Admin: $url/admin" -ForegroundColor Green
            Write-Host "(The URL changes every time the tunnel restarts.)"
            exit 0
        }
        Start-Sleep -Seconds 2
    }
    Write-Host "No URL after 2 minutes; check: docker compose --profile tunnel logs tunnel" -ForegroundColor Yellow
    exit 1
}
finally { Pop-Location }
