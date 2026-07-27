param(
    [string]$TerraformDirectory = "infra/terraform/azure",
    [string]$AiApiKey,
    [switch]$Rotate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$terraformPath = Join-Path $root $TerraformDirectory

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    $result = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
    return $result
}

function New-RandomSecret([int]$ByteCount = 32) {
    $buffer = [byte[]]::new($ByteCount)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($buffer)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($buffer).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Get-KeyVaultSecret([string]$VaultName, [string]$Name) {
    $value = & az keyvault secret show `
        --vault-name $VaultName `
        --name $Name `
        --query value `
        --output tsv `
        --only-show-errors 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return ($value | Out-String).Trim()
}

function Set-KeyVaultSecret([string]$VaultName, [string]$Name, [string]$Value) {
    $null = Invoke-Checked {
        az keyvault secret set `
            --vault-name $VaultName `
            --name $Name `
            --value $Value `
            --output none `
            --only-show-errors
    } "Nao foi possivel gravar o segredo '$Name' no Azure Key Vault."
}

function Get-OrCreateSecret([string]$VaultName, [string]$Name, [int]$ByteCount = 32) {
    if (-not $Rotate) {
        $existing = Get-KeyVaultSecret $VaultName $Name
        if (-not [string]::IsNullOrWhiteSpace($existing)) {
            return $existing
        }
    }

    $generated = New-RandomSecret $ByteCount
    Set-KeyVaultSecret $VaultName $Name $generated
    return $generated
}

$null = Invoke-Checked { az account show --output none --only-show-errors } `
    "Azure CLI nao esta autenticada. Execute 'az login' antes de inicializar os segredos."

$vaultName = (Invoke-Checked {
    terraform -chdir=$terraformPath output -raw key_vault_name
} "Nao foi possivel obter o Key Vault. Aplique o Terraform da Azure primeiro.").Trim()

$postgresPassword = Get-OrCreateSecret $vaultName "postgres-password"
$rabbitPassword = Get-OrCreateSecret $vaultName "rabbitmq-password"
$redisPassword = Get-OrCreateSecret $vaultName "redis-password"
$mongoPassword = Get-OrCreateSecret $vaultName "mongo-password"

$null = Get-OrCreateSecret $vaultName "keycloak-admin-password"
$null = Get-OrCreateSecret $vaultName "keycloak-client-secret" 48
$null = Get-OrCreateSecret $vaultName "grafana-admin-password"
$null = Get-OrCreateSecret $vaultName "zabbix-db-password"
$null = Get-OrCreateSecret $vaultName "payment-webhook-secret" 48
$null = Get-OrCreateSecret $vaultName "demo-manager-password"
$null = Get-OrCreateSecret $vaultName "demo-donor-password"
$null = Get-OrCreateSecret $vaultName "demo-admin-password"
if ([string]::IsNullOrWhiteSpace($AiApiKey)) {
    $null = Get-OrCreateSecret $vaultName "ai-api-key" 48
}
else {
    Set-KeyVaultSecret $vaultName "ai-api-key" $AiApiKey
}

$derivedSecrets = [ordered]@{
    "identity-db-connection"  = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_identity;Username=conexao;Password=$postgresPassword"
    "campaigns-db-connection" = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_campaigns;Username=conexao;Password=$postgresPassword"
    "donations-db-connection" = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_donations;Username=conexao;Password=$postgresPassword"
    "payments-db-connection"  = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_payments;Username=conexao;Password=$postgresPassword"
    "redis-connection"        = "conexao-solidaria-redis:6379,password=$redisPassword,abortConnect=false"
    "mongo-connection"        = "mongodb://conexao:$mongoPassword@conexao-solidaria-mongodb:27017/?authSource=admin"
}

foreach ($entry in $derivedSecrets.GetEnumerator()) {
    if ($Rotate -or [string]::IsNullOrWhiteSpace((Get-KeyVaultSecret $vaultName $entry.Key))) {
        Set-KeyVaultSecret $vaultName $entry.Key $entry.Value
    }
}

Write-Host "Segredos de runtime inicializados no Key Vault '$vaultName' sem exposicao de valores." -ForegroundColor Green
