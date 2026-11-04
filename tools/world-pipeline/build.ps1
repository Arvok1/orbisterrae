param([int]$MaxZoom = 6, [int]$Resolution = 4)
$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "../..")
try {
    docker compose -f tools/world-pipeline/compose.yml run --build --rm world --max-zoom $MaxZoom --resolution $Resolution
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar dados do mundo." }
} finally { Pop-Location }
