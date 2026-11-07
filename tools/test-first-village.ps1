param([switch]$SkipBuild, [switch]$Isolated, [ValidateRange(1024,65535)][int]$Port=18081, [ValidateRange(1024,65535)][int]$NatsPort=14222)
$ErrorActionPreference = "Stop"
$orbisRoot = Split-Path $PSScriptRoot -Parent
$orbisWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$orbisSuffix = if ($orbisWindows) { ".exe" } else { "" }
$orbisDatabase = "orbis_test_" + [Guid]::NewGuid().ToString("N")
if ($orbisDatabase -notmatch '^orbis_test_[a-f0-9]{32}$') { throw "Nome de banco de teste inválido." }
$orbisProcesses = @()
$orbisCreated = $false
$orbisMainRunning = $false
$orbisNatsContainer=$null
$orbisSavedEnv = @{}
foreach ($orbisKey in @("DATABASE_URL","ORBIS_BIND","ORBIS_HTTP_URL","ORBIS_WS_URL","JWT_SECRET","NATS_URL")) { $orbisSavedEnv[$orbisKey] = [Environment]::GetEnvironmentVariable($orbisKey,"Process") }
Push-Location $orbisRoot
try {
    try { $orbisMainRunning = (Invoke-WebRequest http://127.0.0.1:8080/ready -TimeoutSec 2).StatusCode -eq 200 } catch {}
    if ($orbisWindows) { if(-not $Isolated) { & "$orbisRoot/stop.ps1" }; $env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH" }
    if($Isolated) {
        $orbisNatsContainer="orbis-village-test-"+[Guid]::NewGuid().ToString("N")
        & docker run --rm -d --name $orbisNatsContainer -p "127.0.0.1:${NatsPort}:4222" nats:2 | Out-Null
        if($LASTEXITCODE -ne 0) { $orbisNatsContainer=$null;throw "Não foi possível isolar o NATS." }
        $env:NATS_URL="nats://127.0.0.1:$NatsPort"
    }
    if (-not $SkipBuild) {
        cargo build -p orbis-gateway --bins --example test_database
        if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar integração S1.1." }
    }
    pnpm --filter @orbis/protocol build
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar protocolo." }
    pnpm --filter @orbis/bot build
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar bots." }
    $orbisHelper = Join-Path $orbisRoot "target/debug/examples/test_database$orbisSuffix"
    & $orbisHelper create $orbisDatabase
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível criar banco isolado." }
    $orbisCreated = $true
    $orbisUrl = if ($env:DATABASE_URL) { $env:DATABASE_URL } else { "postgres://orbis:orbis@127.0.0.1:5432/orbis?sslmode=disable" }
    $orbisBuilder = [UriBuilder]::new($orbisUrl)
    $orbisBuilder.Path = "/$orbisDatabase"
    $env:DATABASE_URL = $orbisBuilder.Uri.AbsoluteUri
    $env:ORBIS_BIND = "127.0.0.1:$Port"
    $env:ORBIS_HTTP_URL = "http://127.0.0.1:$Port"
    $env:ORBIS_WS_URL = "ws://127.0.0.1:$Port/ws"
    $env:JWT_SECRET = [Guid]::NewGuid().ToString("N") + [Guid]::NewGuid().ToString("N")
    New-Item -ItemType Directory -Path "$orbisRoot/.local" -Force | Out-Null
    foreach ($orbisService in @(@{name="worker0";binary="orbis-shard-worker";args=@("--slot","0")},@{name="worker1";binary="orbis-shard-worker";args=@("--slot","1")},@{name="gateway";binary="orbis-gateway";args=@()})) {
        $orbisExe = [IO.Path]::GetFullPath((Join-Path $orbisRoot "target/debug/$($orbisService.binary)$orbisSuffix"))
        $orbisLaunch = @{FilePath=$orbisExe;WorkingDirectory=$orbisRoot;PassThru=$true;RedirectStandardOutput="$orbisRoot/.local/s1-1-$($orbisService.name).log";RedirectStandardError="$orbisRoot/.local/s1-1-$($orbisService.name).err.log"}
        if ($orbisWindows) { $orbisLaunch.WindowStyle = "Hidden" }
        if ($orbisService.args.Count -gt 0) { $orbisLaunch.ArgumentList = $orbisService.args }
        $orbisProcess = Start-Process @orbisLaunch
        $orbisProcesses += @{id=$orbisProcess.Id;path=$orbisExe;ticks=$orbisProcess.StartTime.ToUniversalTime().Ticks}
    }
    $orbisReady = $false
    for ($orbisAttempt = 0; $orbisAttempt -lt 40; $orbisAttempt++) {
        try { if ((Invoke-WebRequest "$env:ORBIS_HTTP_URL/ready" -TimeoutSec 2).StatusCode -eq 200) { $orbisReady = $true; break } } catch {}
        Start-Sleep -Milliseconds 250
    }
    if (-not $orbisReady) { throw "Serviços de teste indisponíveis; consulte .local/s1-1-*.err.log." }
    node tools/bot/dist/village.js
    if ($LASTEXITCODE -ne 0) { throw "Cenário S1.1 falhou." }
    & $orbisHelper verify $orbisDatabase
    if ($LASTEXITCODE -ne 0) { throw "Invariantes persistidos falharam." }
} finally {
    foreach ($orbisRecord in $orbisProcesses) {
        $orbisProcess = Get-Process -Id $orbisRecord.id -ErrorAction SilentlyContinue
        if ($orbisProcess -and $orbisProcess.Path -eq $orbisRecord.path -and $orbisProcess.StartTime.ToUniversalTime().Ticks -eq $orbisRecord.ticks) { Stop-Process -Id $orbisProcess.Id }
    }
    if ($orbisCreated) { & $orbisHelper drop $orbisDatabase; if ($LASTEXITCODE -ne 0) { Write-Warning "Banco de teste não removido: $orbisDatabase" } }
    foreach ($orbisKey in $orbisSavedEnv.Keys) { [Environment]::SetEnvironmentVariable($orbisKey,$orbisSavedEnv[$orbisKey],"Process") }
    if($orbisNatsContainer) { & docker stop $orbisNatsContainer | Out-Null }
    if ($orbisMainRunning -and $orbisWindows -and -not $Isolated) { & "$orbisRoot/scripts/start-services.ps1" }
    Pop-Location
}
