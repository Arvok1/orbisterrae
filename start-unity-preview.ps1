param(
    [switch]$Rebuild,
    [string]$UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe",
    [string]$HttpEndpoint = "http://127.0.0.1:8080",
    [string]$WebSocketEndpoint = "ws://127.0.0.1:8080/ws",
    [string]$TerrainRoot = "D:\OrbisTerrae\world-v2",
    [ValidateSet("pt-BR", "en")]
    [string]$Locale = "pt-BR"
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "apps\unity-client"
$buildRoot = Join-Path $projectPath "Build\Windows"
$executable = Join-Path $buildRoot "OrbisTerrae.exe"
$buildLog = Join-Path $projectPath "Logs\preview-build.log"

function Test-WorldPackage([string]$Root) {
    $manifestPath = Join-Path $Root "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $false }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.format -ne "orbis-world-v2" -or $manifest.version -ne 2) { throw "Manifesto mundial incompatível em '$manifestPath'." }
    if ([int64]$manifest.package_bytes -gt 8GB) { throw "Pacote mundial excede 8 GB." }
    foreach ($shard in $manifest.shards) {
        $path = Join-Path $Root $shard.path
        if (-not (Test-Path -LiteralPath $path)) { throw "Shard ausente: $path" }
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$shard.bytes) { throw "Tamanho divergente: $path" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $shard.sha256) { throw "Hash divergente: $path" }
    }
    return $true
}

if ($Rebuild -or -not (Test-Path -LiteralPath $executable)) {
    if (-not (Test-Path -LiteralPath $UnityEditorPath)) {
        throw "Unity Editor 6000.6.7f1 não encontrado em '$UnityEditorPath'. Use -UnityEditorPath para informar outro caminho."
    }

    New-Item -ItemType Directory -Force $buildRoot | Out-Null
    New-Item -ItemType Directory -Force (Split-Path $buildLog) | Out-Null
    $argumentLine = "-batchmode -nographics -quit -projectPath `"$projectPath`" -buildTarget win64 -executeMethod Orbis.Editor.BuildClient.BuildWindowsDevelopment --build-output=`"$executable`" -logFile `"$buildLog`""
    $build = Start-Process -FilePath $UnityEditorPath -ArgumentList $argumentLine -PassThru -Wait -WindowStyle Hidden
    if ($build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $executable)) {
        throw "Build Unity falhou com código $($build.ExitCode). Consulte '$buildLog'."
    }
}

$terrainArgument = ""
if (Test-WorldPackage $TerrainRoot) {
    $terrainArgument = " --terrain-root=`"$TerrainRoot`""
} else {
    Write-Warning "Pacote externo não encontrado; o Player usará o globo global embutido. Gere com .\scripts\build-world-pack.ps1."
}
$runtimeArgumentLine = "--http=$HttpEndpoint --ws=$WebSocketEndpoint --locale=$Locale$terrainArgument -screen-fullscreen 0 -screen-width 1280 -screen-height 720"
Start-Process -FilePath $executable -ArgumentList $runtimeArgumentLine -WorkingDirectory $buildRoot
Write-Host "Orbis Terrae iniciado: $executable"
