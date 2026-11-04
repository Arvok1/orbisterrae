param([switch]$NoBrowser)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        throw "PNPM não encontrado. Instale Node.js e PNPM 9 antes de iniciar."
    }
    $orbisWorldReady = $false
    if (Test-Path "data/world/v0/manifest.json") {
        try {
            $orbisManifest = Get-Content "data/world/v0/manifest.json" -Raw | ConvertFrom-Json
            $orbisWorldReady = $orbisManifest.max_zoom -eq 6 -and $orbisManifest.elevation_sha256 -and (Test-Path "data/world/v0/tiles/6/63/63.png")
        } catch { }
    }
    if (-not $orbisWorldReady) {
        Write-Host "Gerando dados do mundo (primeiro start pode demorar)..."
        & "$PSScriptRoot/tools/world-pipeline/build.ps1"
    }
    & pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw "Falha ao instalar dependências." }
    & pnpm --filter @orbis/protocol build
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar protocolo TypeScript." }
    & "$PSScriptRoot/scripts/start-services.ps1"
    $orbisUrl = "http://127.0.0.1:5173"
    $orbisRunning = $false
    try {
        $orbisResponse = Invoke-WebRequest -Uri $orbisUrl -TimeoutSec 2
        $orbisRunning = $orbisResponse.Content -match "Orbis Terrae"
    } catch { }
    if ($orbisRunning) {
        Write-Host "Atlas já disponível em $orbisUrl"
        if (-not $NoBrowser) { Start-Process $orbisUrl }
        return
    }
    Write-Host "Abrindo atlas em $orbisUrl. Use Ctrl+C para encerrar."
    if ($NoBrowser) { & pnpm --filter @orbis/client dev }
    else { & pnpm --filter @orbis/client dev --open }
    if ($LASTEXITCODE -ne 0) { throw "Falha ao iniciar o cliente." }
} finally { Pop-Location }
