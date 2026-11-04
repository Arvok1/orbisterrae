$ErrorActionPreference = "Stop"
$orbisState = Join-Path $PSScriptRoot ".local/services.json"
if (Test-Path $orbisState) {
    foreach ($orbisRecord in @(Get-Content $orbisState -Raw | ConvertFrom-Json)) {
        if ($orbisRecord.name -notin @("orbis-gateway", "orbis-shard-worker", "orbis-shard-worker-0", "orbis-shard-worker-1")) { continue }
        $orbisBinaryName = if ($orbisRecord.name -like "orbis-shard-worker-*") { "orbis-shard-worker" } else { $orbisRecord.name }
        $orbisExpected = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "target/debug/$orbisBinaryName.exe"))
        if ($orbisRecord.path -ne $orbisExpected) { continue }
        $orbisProcess = Get-Process -Id $orbisRecord.pid -ErrorAction SilentlyContinue
        if ($orbisProcess -and $orbisProcess.Path -eq $orbisExpected -and $orbisProcess.StartTime.ToUniversalTime().Ticks -eq $orbisRecord.startedTicks) {
            Stop-Process -Id $orbisProcess.Id
            Write-Host "Encerrado: $($orbisRecord.name)"
        }
    }
}
Write-Host "Os containers Docker e seus volumes foram preservados. Encerre o cliente com Ctrl+C no terminal."
