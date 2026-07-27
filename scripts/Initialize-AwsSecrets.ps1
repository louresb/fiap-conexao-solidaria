param(
    [string]$TerraformDirectory = "infra/terraform/aws",
    [string]$AwsProfile = "default",
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

$null = Invoke-Checked { aws sts get-caller-identity --profile $AwsProfile --output json } `
    "AWS CLI nao esta autenticada no profile '$AwsProfile'. Execute 'aws login --profile $AwsProfile'."

$secretName = (Invoke-Checked {
    terraform -chdir=$terraformPath output -raw runtime_secret_name
} "Nao foi possivel obter o segredo de runtime. Aplique o Terraform AWS com enable_eks=true primeiro.").Trim()

$existing = $null
if (-not $Rotate) {
    $secretString = & aws secretsmanager get-secret-value `
        --secret-id $secretName `
        --profile $AwsProfile `
        --query SecretString `
        --output text 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($secretString)) {
        $existing = $secretString | ConvertFrom-Json
    }
}

function Get-OrCreate([string]$Name, [int]$ByteCount = 32) {
    $property = $existing.PSObject.Properties[$Name]
    if ($property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return [string]$property.Value
    }
    return New-RandomSecret $ByteCount
}

$postgresPassword = Get-OrCreate "postgresPassword"
$rabbitPassword = Get-OrCreate "rabbitmqPassword"
$redisPassword = Get-OrCreate "redisPassword"
$mongoPassword = Get-OrCreate "mongoPassword"

$runtime = [ordered]@{
    postgresPassword      = $postgresPassword
    rabbitmqPassword      = $rabbitPassword
    redisPassword         = $redisPassword
    mongoPassword         = $mongoPassword
    keycloakAdminPassword = Get-OrCreate "keycloakAdminPassword"
    keycloakClientSecret  = Get-OrCreate "keycloakClientSecret" 48
    grafanaAdminPassword  = Get-OrCreate "grafanaAdminPassword"
    zabbixDbPassword      = Get-OrCreate "zabbixDbPassword"
    paymentWebhookSecret  = Get-OrCreate "paymentWebhookSecret" 48
    demoManagerPassword   = Get-OrCreate "demoManagerPassword"
    demoDonorPassword     = Get-OrCreate "demoDonorPassword"
    demoAdminPassword     = Get-OrCreate "demoAdminPassword"
    aiApiKey              = Get-OrCreate "aiApiKey" 48
    identityDbConnection  = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_identity;Username=conexao;Password=$postgresPassword"
    campaignsDbConnection = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_campaigns;Username=conexao;Password=$postgresPassword"
    donationsDbConnection = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_donations;Username=conexao;Password=$postgresPassword"
    paymentsDbConnection  = "Host=conexao-solidaria-postgres;Port=5432;Database=conexao_payments;Username=conexao;Password=$postgresPassword"
    redisConnection       = "conexao-solidaria-redis:6379,password=$redisPassword,abortConnect=false"
    mongoConnection       = "mongodb://conexao:$mongoPassword@conexao-solidaria-mongodb:27017/?authSource=admin"
}

$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) "conexao-solidaria-$([Guid]::NewGuid().ToString('N')).json"
try {
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($runtime | ConvertTo-Json -Compress),
        [Text.UTF8Encoding]::new($false))
    $null = Invoke-Checked {
        aws secretsmanager put-secret-value `
            --secret-id $secretName `
            --secret-string "file://$temporaryPath" `
            --profile $AwsProfile `
            --output none
    } "Nao foi possivel gravar o segredo de runtime no AWS Secrets Manager."
}
finally {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Segredos de runtime inicializados no AWS Secrets Manager sem exposicao de valores." -ForegroundColor Green
