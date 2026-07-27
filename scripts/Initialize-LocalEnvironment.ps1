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
    $missingSecrets = @{
        KEYCLOAK_CLIENT_SECRET = { New-HexSecret 32 }
        ZABBIX_ADMIN_PASSWORD = { "Cs1!$(New-HexSecret 12)" }
        DEMO_ADMIN_PASSWORD = { "Cs1!$(New-HexSecret 8)" }
        AI_ENABLED = { "false" }
        AI_ENDPOINT = { "" }
        AI_MODEL = { "" }
        AI_API_KEY = { New-HexSecret 32 }
        AI_API_KEY_HEADER = { "api-key" }
    }
    $added = @()

    foreach ($name in $missingSecrets.Keys) {
        if (-not $existing.ContainsKey($name)) {
            $value = & $missingSecrets[$name]
            Add-Content -Path $environmentFile -Value "$name=$value" -Encoding UTF8
            $added += $name
        }
    }

    if ($added.Count -eq 0) {
        Write-Host "O arquivo .env ja existe. Use -Force para gerar novas credenciais."
    }
    else {
        Write-Host "Credenciais locais adicionadas ao .env: $($added -join ', ')."
    }

    exit 0
}

$managerPassword = "Cs1!$(New-HexSecret 8)"
$donorPassword = "Cs1!$(New-HexSecret 8)"
$adminPassword = "Cs1!$(New-HexSecret 8)"
$lines = @(
    "POSTGRES_PASSWORD=$(New-HexSecret)",
    "RABBITMQ_PASSWORD=$(New-HexSecret)",
    "REDIS_PASSWORD=$(New-HexSecret)",
    "MONGO_PASSWORD=$(New-HexSecret)",
    "KEYCLOAK_ADMIN_PASSWORD=$(New-HexSecret)",
    "KEYCLOAK_CLIENT_SECRET=$(New-HexSecret 32)",
    "GRAFANA_ADMIN_PASSWORD=$(New-HexSecret)",
    "ZABBIX_DB_PASSWORD=$(New-HexSecret)",
    "ZABBIX_ADMIN_PASSWORD=Cs1!$(New-HexSecret 12)",
    "PAYMENT_WEBHOOK_SECRET=$(New-HexSecret)",
    "DEMO_MANAGER_PASSWORD=$managerPassword",
    "DEMO_DONOR_PASSWORD=$donorPassword",
    "DEMO_ADMIN_PASSWORD=$adminPassword",
    "AI_ENABLED=false",
    "AI_ENDPOINT=",
    "AI_MODEL=",
    "AI_API_KEY=$(New-HexSecret 32)",
    "AI_API_KEY_HEADER=api-key"
)

[System.IO.File]::WriteAllLines(
    $environmentFile,
    $lines,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Credenciais locais geradas em $environmentFile"
Write-Host "Gestores: gestor.<tenant>@conexaosolidaria.local / $managerPassword"
Write-Host "Doadores: doador.<tenant>@conexaosolidaria.local / $donorPassword"
Write-Host "Admin: admin.plataforma@conexaosolidaria.local / $adminPassword"
