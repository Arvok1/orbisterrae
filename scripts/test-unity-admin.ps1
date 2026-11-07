$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$httpEndpoint=if($env:ORBIS_HTTP_URL) { $env:ORBIS_HTTP_URL } else { "http://127.0.0.1:8080" }
$username = "adminsmoke_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
$password = "Smoke_" + [Guid]::NewGuid().ToString("N")
Push-Location $repoRoot
try {
    $body = @{username=$username;password=$password} | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri "$httpEndpoint/api/register" -ContentType "application/json" -Body $body | Out-Null
    & cargo run -p orbis-gateway --bin orbis-admin -- role grant --username $username --reason "automated admin smoke"
    if ($LASTEXITCODE -ne 0) { throw "Admin grant failed." }
    & pnpm --filter @orbis/protocol build
    & pnpm --filter @orbis/bot build
    if ($LASTEXITCODE -ne 0) { throw "Bot build failed." }
    $env:ORBIS_ADMIN_TEST_USER = $username
    $env:ORBIS_ADMIN_TEST_PASSWORD = $password
    $env:ORBIS_EXPECT_ADMIN = "true"
    & node tools/bot/dist/adminSmoke.js
    if ($LASTEXITCODE -ne 0) { throw "Granted admin smoke failed." }
    & cargo run -p orbis-gateway --bin orbis-admin -- role revoke --username $username --reason "automated revocation smoke"
    if ($LASTEXITCODE -ne 0) { throw "Admin revoke failed." }
    $env:ORBIS_EXPECT_ADMIN = "false"
    & node tools/bot/dist/adminSmoke.js
    if ($LASTEXITCODE -ne 0) { throw "Revoked admin smoke failed." }
} finally {
    foreach ($name in @("ORBIS_ADMIN_TEST_USER","ORBIS_ADMIN_TEST_PASSWORD","ORBIS_EXPECT_ADMIN")) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    $password = $null
    $body = $null
    Pop-Location
}
