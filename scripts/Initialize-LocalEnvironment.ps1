param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $root ".env"

if ((Test-Path $environmentFile) -and -not $Force) {
    Write-Host "O arquivo .env já existe. Use -Force para gerar novas credenciais."
    exit 0
}

function New-HexSecret([int]$ByteCount = 24) {
    $bytes = [byte[]]::new($ByteCount)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return ([BitConverter]::ToString($bytes) -replace "-", "").ToLowerInvariant()
}

$managerPassword = "Cs1!$(New-HexSecret 8)"
$donorPassword = "Cs1!$(New-HexSecret 8)"
$lines = @(
    "POSTGRES_PASSWORD=$(New-HexSecret)",
    "RABBITMQ_PASSWORD=$(New-HexSecret)",
    "REDIS_PASSWORD=$(New-HexSecret)",
    "MONGO_PASSWORD=$(New-HexSecret)",
    "KEYCLOAK_ADMIN_PASSWORD=$(New-HexSecret)",
    "GRAFANA_ADMIN_PASSWORD=$(New-HexSecret)",
    "ZABBIX_DB_PASSWORD=$(New-HexSecret)",
    "PAYMENT_WEBHOOK_SECRET=$(New-HexSecret)",
    "DEMO_MANAGER_PASSWORD=$managerPassword",
    "DEMO_DONOR_PASSWORD=$donorPassword"
)

[System.IO.File]::WriteAllLines(
    $environmentFile,
    $lines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Credenciais locais geradas em $environmentFile"
Write-Host "Gestores: gestor.<tenant>@conexaosolidaria.local / $managerPassword"
Write-Host "Doadores: doador.<tenant>@conexaosolidaria.local / $donorPassword"
