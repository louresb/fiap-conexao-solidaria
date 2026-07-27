param(
    [switch]$PurgeData
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($PurgeData) {
    docker compose --profile tools --profile ops down --volumes --remove-orphans
}
else {
    docker compose --profile tools --profile ops down --remove-orphans
}

if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel encerrar o ambiente local."
}

Write-Host "Ambiente encerrado. Dados persistentes $($(if ($PurgeData) { 'removidos' } else { 'preservados' }))."
