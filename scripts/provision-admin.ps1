param(
    [ValidateSet("Grant", "Revoke")]
    [string]$Action = "Grant",
    [string]$Username = "admin",
    [string]$Reason = "local Unity observatory",
    [string]$HttpEndpoint = "http://127.0.0.1:8080"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    if ($Username -notmatch '^[A-Za-z0-9_.-]{3,32}$') {
        throw "Usuário inválido: use 3–32 letras, números, ponto, hífen ou sublinhado."
    }

    if ($Action -eq "Grant") {
        $securePassword = Read-Host "Senha da conta '$Username' (mínimo 8 caracteres)" -AsSecureString
        $credential = [PSCredential]::new($Username, $securePassword)
        $password = $credential.GetNetworkCredential().Password
        if ($password.Length -lt 8) { throw "A senha precisa ter ao menos 8 caracteres." }
        $body = @{ username = $Username; password = $password } | ConvertTo-Json -Compress
        try {
            Invoke-RestMethod -Method Post -Uri "$HttpEndpoint/api/login" -ContentType "application/json" -Body $body -TimeoutSec 10 | Out-Null
        } catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($status -ne 401) { throw }
            Invoke-RestMethod -Method Post -Uri "$HttpEndpoint/api/register" -ContentType "application/json" -Body $body -TimeoutSec 15 | Out-Null
        } finally {
            $password = $null
            $body = $null
            $credential = $null
        }
    }

    $verb = $Action.ToLowerInvariant()
    & cargo run -p orbis-gateway --bin orbis-admin -- role $verb --username $Username --reason $Reason
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível alterar o papel administrativo." }
    Write-Host "Papel atualizado. Entre no Unity como '$Username'."
} finally {
    Pop-Location
}
