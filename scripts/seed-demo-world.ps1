param([string]$HttpEndpoint = "http://127.0.0.1:8080", [string]$WebSocketEndpoint = "ws://127.0.0.1:8080/ws")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    # curl em vez de Invoke-WebRequest: um proxy local pode responder 503 a endereços loopback (mesma causa da correção em tools/test-city.ps1).
    if ((& curl.exe -s -m 3 "$HttpEndpoint/ready") -ne "orbis-gateway-worker-ready") {
        throw "Gateway local indisponível em $HttpEndpoint. Inicie os serviços antes de semear o mundo demo."
    }
    & pnpm --filter @orbis/protocol build
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar protocolo." }
    & pnpm --filter @orbis/bot build
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar bot." }
    $env:ORBIS_HTTP_URL = $HttpEndpoint
    $env:ORBIS_WS_URL = $WebSocketEndpoint
    & node tools/bot/dist/demoWorld.js
    if ($LASTEXITCODE -ne 0) { throw "Falha ao semear o cenário demonstrativo." }
} finally {
    Remove-Item Env:ORBIS_HTTP_URL -ErrorAction SilentlyContinue
    Remove-Item Env:ORBIS_WS_URL -ErrorAction SilentlyContinue
    Pop-Location
}
