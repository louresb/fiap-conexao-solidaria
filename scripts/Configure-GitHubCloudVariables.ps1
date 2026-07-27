param(
    [Parameter(Mandatory)]
    [ValidateSet("azure", "aws")]
    [string]$Provider,
    [string]$Repository = "louresb/fiap-conexao-solidaria",
    [string]$AzureTerraformDirectory = "infra/terraform/azure",
    [string]$AwsTerraformDirectory = "infra/terraform/aws",
    [string]$AwsAppHost,
    [string]$KubernetesNamespace = "conexao-solidaria",
    [switch]$EnablePublish,
    [switch]$EnableDeploy,
    [switch]$ConfirmGitHubMutation
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $ConfirmGitHubMutation) {
    throw "O script altera variables do GitHub. Revise os parametros e repita com -ConfirmGitHubMutation."
}

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    $result = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
    return $result
}

function Read-TerraformOutputs([string]$Directory) {
    $json = Invoke-Checked {
        terraform -chdir=$Directory output -json
    } "Nao foi possivel ler os outputs Terraform em '$Directory'. Aplique a infraestrutura primeiro."

    return ($json | Out-String | ConvertFrom-Json)
}

function Read-OutputValue([object]$Outputs, [string]$Name, [switch]$Optional) {
    $property = $Outputs.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value.value) {
        if ($Optional) {
            return $null
        }
        throw "O output Terraform obrigatorio '$Name' nao foi encontrado."
    }

    return [string]$property.Value.value
}

function Set-RepositoryVariable([string]$Name, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $null = Invoke-Checked {
        gh variable set $Name --repo $Repository --body $Value
    } "Nao foi possivel configurar a variable '$Name' em '$Repository'."
}

$null = Invoke-Checked { gh auth status } "GitHub CLI nao esta autenticada. Execute 'gh auth login'."
Set-RepositoryVariable "K8S_NAMESPACE" $KubernetesNamespace

if ($Provider -eq "azure") {
    $outputs = Read-TerraformOutputs $AzureTerraformDirectory
    $hostName = Read-OutputValue $outputs "application_hostname"

    Set-RepositoryVariable "AZURE_CLIENT_ID" (Read-OutputValue $outputs "github_azure_client_id")
    Set-RepositoryVariable "AZURE_TENANT_ID" (Read-OutputValue $outputs "github_azure_tenant_id")
    Set-RepositoryVariable "AZURE_SUBSCRIPTION_ID" (Read-OutputValue $outputs "github_azure_subscription_id")
    Set-RepositoryVariable "ACR_NAME" (Read-OutputValue $outputs "container_registry_name")
    Set-RepositoryVariable "AKS_RESOURCE_GROUP" (Read-OutputValue $outputs "resource_group_name")
    Set-RepositoryVariable "AKS_CLUSTER" (Read-OutputValue $outputs "cluster_name")
    Set-RepositoryVariable "AZURE_INGRESS_PUBLIC_IP_NAME" (Read-OutputValue $outputs "ingress_public_ip_name")
    Set-RepositoryVariable "AZURE_APP_HOST" $hostName
    Set-RepositoryVariable "ENABLE_AZURE_PUBLISH" $EnablePublish.IsPresent.ToString().ToLowerInvariant()
    Set-RepositoryVariable "ENABLE_AZURE_DEPLOY" $EnableDeploy.IsPresent.ToString().ToLowerInvariant()
}
else {
    $outputs = Read-TerraformOutputs $AwsTerraformDirectory
    $clusterName = Read-OutputValue $outputs "cluster_name" -Optional
    $deployRoleArn = Read-OutputValue $outputs "github_actions_eks_role_arn" -Optional

    if ($EnableDeploy -and
        ([string]::IsNullOrWhiteSpace($clusterName) -or
         [string]::IsNullOrWhiteSpace($deployRoleArn) -or
         [string]::IsNullOrWhiteSpace($AwsAppHost))) {
        throw "Deploy AWS requer EKS aplicado, role OIDC de deploy e -AwsAppHost."
    }

    Set-RepositoryVariable "AWS_ACCOUNT_ID" (Read-OutputValue $outputs "aws_account_id")
    Set-RepositoryVariable "AWS_REGION" (Read-OutputValue $outputs "aws_region")
    Set-RepositoryVariable "AWS_GITHUB_PUBLISH_ROLE_ARN" (Read-OutputValue $outputs "github_actions_ecr_role_arn")
    Set-RepositoryVariable "AWS_GITHUB_DEPLOY_ROLE_ARN" $deployRoleArn
    Set-RepositoryVariable "EKS_CLUSTER" $clusterName
    Set-RepositoryVariable "AWS_APP_HOST" $AwsAppHost
    Set-RepositoryVariable "ENABLE_AWS_PUBLISH" $EnablePublish.IsPresent.ToString().ToLowerInvariant()
    Set-RepositoryVariable "ENABLE_AWS_DEPLOY" $EnableDeploy.IsPresent.ToString().ToLowerInvariant()
}

Write-Host "Variables de $Provider configuradas em $Repository; secrets nao foram criados." -ForegroundColor Green
