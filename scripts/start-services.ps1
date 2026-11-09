$ErrorActionPreference = "Stop"
$orbisRoot = Split-Path $PSScriptRoot -Parent
Push-Location $orbisRoot
try {
    $orbisCargo = Join-Path $env:USERPROFILE ".cargo/bin/cargo.exe"
    if (-not (Test-Path $orbisCargo)) { throw "Cargo não encontrado. Instale Rust via rustup." }
    docker compose -f infra/docker-compose.yml up -d
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível iniciar a infraestrutura. Abra Docker Desktop." }
    $orbisInputs = @(Get-ChildItem crates -Recurse -File | Where-Object { $_.Extension -eq ".rs" -or $_.Name -eq "Cargo.toml" })
    $orbisInputs += Get-Item Cargo.toml,Cargo.lock,packages/protocol/orbis.proto
    $orbisInputs += Get-ChildItem config/balance -Filter *.toml -File
    $orbisInputs += Get-ChildItem infra/migrations -Filter *.sql -File
    $orbisInputs += Get-Item apps/client/src/assets/world-land-v1.json,apps/client/src/assets/world-terrain-v1.json
    $orbisLatestInput = ($orbisInputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    $orbisNeedsBuild = $false
    foreach ($orbisBinaryName in @("orbis-shard-worker", "orbis-gateway")) {
        $orbisBinaryPath = Join-Path $orbisRoot "target/debug/$orbisBinaryName.exe"
        if (-not (Test-Path $orbisBinaryPath) -or (Get-Item $orbisBinaryPath).LastWriteTimeUtc -lt $orbisLatestInput) { $orbisNeedsBuild = $true }
    }
    if ($orbisNeedsBuild) {
        & "$orbisRoot/stop.ps1"
        & $orbisCargo build -p orbis-gateway --bins
        if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar serviços Rust." }
    }
    $orbisReady = $false
    # curl em vez de Invoke-WebRequest: um proxy local pode responder 503 ao endereço loopback (mesma causa da correção em tools/test-city.ps1).
    $orbisReady = (& curl.exe -s -m 3 "http://127.0.0.1:8080/ready") -eq "orbis-gateway-worker-ready"
    if ($orbisReady) { Write-Host "Gateway e worker já estão prontos."; return }
    if (-not $orbisNeedsBuild) { & "$orbisRoot/stop.ps1" }
    $orbisLogDir = Join-Path $orbisRoot ".local"
    New-Item -ItemType Directory -Path $orbisLogDir -Force | Out-Null
    $orbisRecords = @()
    if (Test-Path "$orbisLogDir/services.json") {
        $orbisRecords = @(Get-Content "$orbisLogDir/services.json" -Raw | ConvertFrom-Json)
    }
    $orbisServices = @(
        @{name="orbis-shard-worker-0";binary="orbis-shard-worker";arguments=@("--slot","0")},
        @{name="orbis-shard-worker-1";binary="orbis-shard-worker";arguments=@("--slot","1")},
        @{name="orbis-gateway";binary="orbis-gateway";arguments=@()}
    )
    foreach ($orbisService in $orbisServices) {
        $orbisName = $orbisService.name
        $orbisExe = [System.IO.Path]::GetFullPath((Join-Path $orbisRoot "target/debug/$($orbisService.binary).exe"))
        $orbisExisting = $orbisRecords | Where-Object { $_.name -eq $orbisName } | Select-Object -First 1
        $orbisProcess = $null
        if ($orbisExisting) {
            $orbisCandidate = Get-Process -Id $orbisExisting.pid -ErrorAction SilentlyContinue
            if ($orbisCandidate -and $orbisCandidate.Path -eq $orbisExe -and $orbisCandidate.StartTime.ToUniversalTime().Ticks -eq $orbisExisting.startedTicks) {
                $orbisProcess = $orbisCandidate
            }
        }
        if (-not $orbisProcess) {
            $orbisLaunch = @{FilePath=$orbisExe;WorkingDirectory=$orbisRoot;WindowStyle="Hidden";PassThru=$true;RedirectStandardOutput="$orbisLogDir/$orbisName.log";RedirectStandardError="$orbisLogDir/$orbisName.err.log"}
            if ($orbisService.arguments.Count -gt 0) { $orbisLaunch.ArgumentList = $orbisService.arguments }
            $orbisProcess = Start-Process @orbisLaunch
            $orbisRecords = @($orbisRecords | Where-Object { $_.name -ne $orbisName })
            $orbisRecords += [pscustomobject]@{name=$orbisName; pid=$orbisProcess.Id; path=$orbisExe; startedTicks=$orbisProcess.StartTime.ToUniversalTime().Ticks}
        }
        ConvertTo-Json -InputObject @($orbisRecords) | Set-Content "$orbisLogDir/services.json"
    }
    for ($orbisAttempt = 0; $orbisAttempt -lt 30; $orbisAttempt++) {
        # curl em vez de Invoke-WebRequest: um proxy local pode responder 503 a endereços loopback.
        if ((& curl.exe -s -m 2 "http://127.0.0.1:8080/ready") -eq "orbis-gateway-worker-ready") {
            Write-Host "Gateway e worker NATS prontos."
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Serviços não ficaram prontos; consulte .local/*.err.log."
} finally { Pop-Location }
