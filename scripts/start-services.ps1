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
    try { $orbisReady = (Invoke-WebRequest "http://127.0.0.1:8080/ready" -TimeoutSec 3).Content -eq "orbis-gateway-worker-ready" } catch {}
    if ($orbisReady) { Write-Host "Gateway e worker já estão prontos."; return }
    $orbisLogDir = Join-Path $orbisRoot ".local"
    New-Item -ItemType Directory -Path $orbisLogDir -Force | Out-Null
    $orbisRecords = @()
    if (Test-Path "$orbisLogDir/services.json") {
        $orbisRecords = @(Get-Content "$orbisLogDir/services.json" -Raw | ConvertFrom-Json)
    }
    foreach ($orbisName in @("orbis-shard-worker", "orbis-gateway")) {
        $orbisExe = [System.IO.Path]::GetFullPath((Join-Path $orbisRoot "target/debug/$orbisName.exe"))
        $orbisExisting = $orbisRecords | Where-Object { $_.name -eq $orbisName } | Select-Object -First 1
        $orbisProcess = $null
        if ($orbisExisting) {
            $orbisCandidate = Get-Process -Id $orbisExisting.pid -ErrorAction SilentlyContinue
            if ($orbisCandidate -and $orbisCandidate.Path -eq $orbisExe -and $orbisCandidate.StartTime.ToUniversalTime().Ticks -eq $orbisExisting.startedTicks) {
                $orbisProcess = $orbisCandidate
            }
        }
        if (-not $orbisProcess) {
            $orbisProcess = Start-Process -FilePath $orbisExe -WorkingDirectory $orbisRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput "$orbisLogDir/$orbisName.log" -RedirectStandardError "$orbisLogDir/$orbisName.err.log"
            $orbisRecords = @($orbisRecords | Where-Object { $_.name -ne $orbisName })
            $orbisRecords += [pscustomobject]@{name=$orbisName; pid=$orbisProcess.Id; path=$orbisExe; startedTicks=$orbisProcess.StartTime.ToUniversalTime().Ticks}
        }
        ConvertTo-Json -InputObject @($orbisRecords) | Set-Content "$orbisLogDir/services.json"
    }
    for ($orbisAttempt = 0; $orbisAttempt -lt 30; $orbisAttempt++) {
        try {
            if ((Invoke-WebRequest "http://127.0.0.1:8080/ready" -TimeoutSec 2).Content -eq "orbis-gateway-worker-ready") {
                Write-Host "Gateway e worker NATS prontos."
                return
            }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    throw "Serviços não ficaram prontos; consulte .local/*.err.log."
} finally { Pop-Location }
