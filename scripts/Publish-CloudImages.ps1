param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("azure", "aws")]
    [string]$Provider,
    [string]$ImageTag,
    [string]$AwsProfile = "conexao-solidaria"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    $result = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
    return $result
}

if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = (& git rev-parse --short=12 HEAD | Out-String).Trim()
}
if ($ImageTag -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') {
    throw "ImageTag nao e uma tag OCI valida."
}

$components = [ordered]@{
    "web"              = "src/Web/ConexaoSolidaria.Web/Dockerfile"
    "gateway"          = "src/Gateway/ConexaoSolidaria.Gateway/Dockerfile"
    "identity-api"     = "src/Services/Identity/ConexaoSolidaria.Identity.Api/Dockerfile"
    "campaigns-api"    = "src/Services/Campaigns/ConexaoSolidaria.Campaigns.Api/Dockerfile"
    "campaigns-worker" = "src/Services/Campaigns/ConexaoSolidaria.Campaigns.Worker/Dockerfile"
    "donations-api"    = "src/Services/Donations/ConexaoSolidaria.Donations.Api/Dockerfile"
    "payments-api"     = "src/Services/Payments/ConexaoSolidaria.Payments.Api/Dockerfile"
    "audit-api"        = "src/Services/Audit/ConexaoSolidaria.Audit.Api/Dockerfile"
    "knowledge-api"    = "src/Services/Knowledge/ConexaoSolidaria.Knowledge.Api/Dockerfile"
}

if ($Provider -eq "azure") {
    $registryName = (Invoke-Checked {
        terraform -chdir=infra/terraform/azure output -raw container_registry_name
    } "Nao foi possivel ler o ACR dos outputs Terraform.").Trim()
    $registry = (Invoke-Checked {
        terraform -chdir=infra/terraform/azure output -raw container_registry_login_server
    } "Nao foi possivel ler o login server do ACR.").Trim()
    $null = Invoke-Checked { az acr login --name $registryName --only-show-errors } `
        "Nao foi possivel autenticar no ACR."
}
else {
    $registry = (Invoke-Checked {
        terraform -chdir=infra/terraform/aws output -raw ecr_registry
    } "Nao foi possivel ler o ECR dos outputs Terraform.").Trim()
    $region = (Invoke-Checked {
        terraform -chdir=infra/terraform/aws output -raw aws_region
    } "Nao foi possivel ler a regiao AWS dos outputs Terraform.").Trim()
    aws ecr get-login-password --region $region --profile $AwsProfile |
        docker login --username AWS --password-stdin $registry
    if ($LASTEXITCODE -ne 0) {
        throw "Nao foi possivel autenticar no ECR."
    }
}

foreach ($component in $components.GetEnumerator()) {
    $image = "$registry/conexao-solidaria-$($component.Key):$ImageTag"
    Write-Host "Publicando $image..." -ForegroundColor Cyan
    $null = Invoke-Checked {
        docker buildx build `
            --platform linux/amd64 `
            --file $component.Value `
            --tag $image `
            --push `
            .
    } "Falha ao construir ou publicar $image."
}

Write-Host "Nove imagens publicadas em $registry com a tag '$ImageTag'." -ForegroundColor Green
