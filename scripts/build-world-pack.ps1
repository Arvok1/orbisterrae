param(
    [string]$Output = "D:\OrbisTerrae\world-v2",
    [ValidateRange(0, 7)]
    [int]$MaxLod = 5
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputParent = Split-Path $Output -Parent
New-Item -ItemType Directory -Force $outputParent | Out-Null

$relativeOutput = $Output.Substring(3).Replace("\", "/")
$dockerOutput = "/external/$relativeOutput"
$arguments = @(
    "compose", "-f", "tools/world-pipeline/compose.yml", "run", "--rm",
    "-v", "D:/:/external", "--entrypoint", "python", "world",
    "tools/world-pipeline/build_world_v2.py",
    "--elevation", "data/world/v0/etopo_2022_60s.tif",
    "--relief", "apps/client/src/assets/world-relief-v2.png",
    "--biomes", "apps/client/src/assets/world-biomes-v1.png",
    "--hydrography", "apps/client/src/assets/world-terrain-v1.json",
    "--output", $dockerOutput,
    "--max-lod", $MaxLod
)

Push-Location $repoRoot
try {
    & docker @arguments
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar orbis-world-v2 (docker: $LASTEXITCODE)." }
    $manifest = Join-Path $Output "manifest.json"
    if (-not (Test-Path -LiteralPath $manifest)) { throw "Manifesto não gerado em '$manifest'." }
    Write-Host "Pacote mundial pronto: $manifest"
}
finally {
    Pop-Location
}
