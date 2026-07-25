param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$environmentFile = Join-Path $root ".env"

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

if ((Test-Path $environmentFile) -and -not $Force) {
    $existing = Get-Content $environmentFile -Raw | ConvertFrom-StringData
    if (-not $existing.ContainsKey("KEYCLOAK_CLIENT_SECRET")) {
        Add-Content -Path $environmentFile -Value "KEYCLOAK_CLIENT_SECRET=$(New-HexSecret 32)" -Encoding UTF8
        Write-Host "Segredo confidencial do cliente Keycloak adicionado ao .env."
    }
    else {
        Write-Host "O arquivo .env ja existe. Use -Force para gerar novas credenciais."
    }

    exit 0
}

$managerPassword = "Cs1!$(New-HexSecret 8)"
$donorPassword = "Cs1!$(New-HexSecret 8)"
$lines = @(
    "POSTGRES_PASSWORD=$(New-HexSecret)",
    "RABBITMQ_PASSWORD=$(New-HexSecret)",
    "REDIS_PASSWORD=$(New-HexSecret)",
    "MONGO_PASSWORD=$(New-HexSecret)",
    "KEYCLOAK_ADMIN_PASSWORD=$(New-HexSecret)",
    "KEYCLOAK_CLIENT_SECRET=$(New-HexSecret 32)",
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
