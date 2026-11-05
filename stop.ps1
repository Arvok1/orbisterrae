$ErrorActionPreference = "Stop"
$orbisRoot = Split-Path $PSScriptRoot -Parent
foreach ($orbisName in @("orbis-gateway", "orbis-shard-worker")) {
    Get-Process -Name $orbisName -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($orbisRoot, [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        Stop-Process -Id $_.Id
        Write-Host "Encerrado: $orbisName ($($_.Id))"
    }
}
Write-Host "Os containers Docker e seus volumes foram preservados. Encerre o cliente com Ctrl+C no terminal."
