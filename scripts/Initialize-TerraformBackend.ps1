param(
    [Parameter(Mandatory)]
    [ValidateSet("azure", "aws")]
    [string]$Provider,
    [string]$Environment = "demo",
    [string]$AzureLocation = "brazilsouth",
    [string]$AzureSuffix = "blouresfiap26",
    [string]$ExpectedAzureSubscriptionId,
    [string]$AwsProfile = "conexao-solidaria-terraform",
    [string]$AwsRegion = "us-east-1",
    [string]$ExpectedAwsAccountId,
    [switch]$MigrateExistingState,
    [switch]$ConfirmCloudMutation
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$stateDirectory = Join-Path $root ".local/terraform-backends"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
Set-Location $root

if (-not $ConfirmCloudMutation) {
    throw "O bootstrap cria armazenamento cloud. Revise os nomes e repita com -ConfirmCloudMutation."
}

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    $result = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
    return $result
}

function Initialize-Terraform([string]$TerraformPath, [string]$BackendPath) {
    $arguments = @("-chdir=$TerraformPath", "init", "-backend-config=$BackendPath")
    if ($MigrateExistingState) {
        $arguments += @("-migrate-state", "-force-copy")
    }
    else {
        $arguments += "-reconfigure"
    }

    $null = Invoke-Checked { terraform @arguments } `
        "O Terraform nao conseguiu inicializar o backend remoto."
}

function Initialize-AzureBackend {
    $subscriptionId = (Invoke-Checked {
        az account show --query id --output tsv --only-show-errors
    } "Azure CLI nao autenticada. Execute 'az login'." | Out-String).Trim()
    $principalId = (Invoke-Checked {
        az ad signed-in-user show --query id --output tsv --only-show-errors
    } "O bootstrap Azure requer uma sessao interativa de usuario." | Out-String).Trim()

    if ($ExpectedAzureSubscriptionId -and $subscriptionId -ne $ExpectedAzureSubscriptionId) {
        throw "Assinatura Azure incorreta. Atual: '$subscriptionId'; esperada: '$ExpectedAzureSubscriptionId'."
    }

    $normalizedSuffix = ($AzureSuffix -replace "[^a-zA-Z0-9]", "").ToLowerInvariant()
    $storageAccount = "stcs$normalizedSuffix"
    if ($storageAccount.Length -gt 24) {
        $storageAccount = $storageAccount.Substring(0, 24)
    }
    if ($storageAccount.Length -lt 3) {
        throw "AzureSuffix deve produzir um nome de Storage Account com pelo menos 3 caracteres."
    }

    $resourceGroup = "rg-conexao-solidaria-tfstate"
    $container = "tfstate"
    $key = "conexao-solidaria/$Environment/azure.tfstate"

    $resourceGroupExists = (Invoke-Checked {
        az group exists `
            --name $resourceGroup `
            --output tsv `
            --only-show-errors
    } "Nao foi possivel consultar o Resource Group do estado Terraform." | Out-String).Trim()
    if ($resourceGroupExists -ne "true") {
        $null = Invoke-Checked {
            az group create `
                --name $resourceGroup `
                --location $AzureLocation `
                --tags project=conexao-solidaria purpose=terraform-state managed-by=bootstrap `
                --output none `
                --only-show-errors
        } "Nao foi possivel criar o Resource Group do estado Terraform."
    }

    $existingStorageAccount = (Invoke-Checked {
        az storage account list `
            --resource-group $resourceGroup `
            --query "[?name=='$storageAccount'].name" `
            --output tsv `
            --only-show-errors
    } "Nao foi possivel consultar os Storage Accounts do Resource Group." | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($existingStorageAccount)) {
        $availability = (Invoke-Checked {
            az storage account check-name --name $storageAccount --query nameAvailable --output tsv --only-show-errors
        } "Nao foi possivel validar o nome do Storage Account." | Out-String).Trim()
        if ($availability -ne "true") {
            throw "O Storage Account '$storageAccount' nao esta disponivel. Use outro AzureSuffix."
        }

        $null = Invoke-Checked {
            az storage account create `
                --name $storageAccount `
                --resource-group $resourceGroup `
                --location $AzureLocation `
                --sku Standard_LRS `
                --kind StorageV2 `
                --https-only true `
                --min-tls-version TLS1_2 `
                --allow-blob-public-access false `
                --allow-shared-key-access false `
                --output none `
                --only-show-errors
        } "Nao foi possivel criar o Storage Account do estado Terraform."
    }

    $null = Invoke-Checked {
        az storage account blob-service-properties update `
            --account-name $storageAccount `
            --resource-group $resourceGroup `
            --enable-versioning true `
            --enable-delete-retention true `
            --delete-retention-days 30 `
            --enable-container-delete-retention true `
            --container-delete-retention-days 30 `
            --output none `
            --only-show-errors
    } "Nao foi possivel habilitar versionamento e retencao no Blob Storage."

    $storageScope = (Invoke-Checked {
        az storage account show `
            --name $storageAccount `
            --resource-group $resourceGroup `
            --query id `
            --output tsv `
            --only-show-errors
    } "Nao foi possivel consultar o Storage Account." | Out-String).Trim()

    $existingAssignments = (Invoke-Checked {
        az role assignment list `
            --assignee-object-id $principalId `
            --role "Storage Blob Data Contributor" `
            --scope $storageScope `
            --query "[].id" `
            --output tsv `
            --only-show-errors
    } "Nao foi possivel consultar o acesso ao Storage Account." | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($existingAssignments)) {
        $null = Invoke-Checked {
            az role assignment create `
                --assignee-object-id $principalId `
                --assignee-principal-type User `
                --role "Storage Blob Data Contributor" `
                --scope $storageScope `
                --output none `
                --only-show-errors
        } "Nao foi possivel conceder acesso de dados ao usuario atual."
    }

    $containerCreated = $false
    for ($attempt = 1; $attempt -le 12 -and -not $containerCreated; $attempt++) {
        try {
            az storage container create `
                --name $container `
                --account-name $storageAccount `
                --auth-mode login `
                --output none `
                --only-show-errors 2>$null
            $containerCreated = $LASTEXITCODE -eq 0
        }
        catch {
            $containerCreated = $false
        }

        if (-not $containerCreated) {
            Start-Sleep -Seconds 5
        }
    }
    if (-not $containerCreated) {
        throw "A permissao de dados Azure nao propagou a tempo para criar o container."
    }

    $backendPath = Join-Path $stateDirectory "azure-$Environment.hcl"
    $backend = @"
resource_group_name  = "$resourceGroup"
storage_account_name = "$storageAccount"
container_name       = "$container"
key                  = "$key"
use_azuread_auth     = true
subscription_id      = "$subscriptionId"
"@
    [System.IO.File]::WriteAllText($backendPath, $backend, [System.Text.UTF8Encoding]::new($false))
    Initialize-Terraform "infra/terraform/azure" $backendPath

    Write-Host "Backend Azure inicializado: $storageAccount/$container/$key" -ForegroundColor Green
}

function Initialize-AwsBackend {
    $identity = (Invoke-Checked {
        aws sts get-caller-identity --profile $AwsProfile --output json
    } "AWS CLI nao autenticada. Execute 'aws login --profile $AwsProfile'." | Out-String | ConvertFrom-Json)
    $accountId = $identity.Account
    if ($ExpectedAwsAccountId -and $accountId -ne $ExpectedAwsAccountId) {
        throw "Conta AWS incorreta. Atual: '$accountId'; esperada: '$ExpectedAwsAccountId'."
    }

    $bucket = "conexao-solidaria-tfstate-$accountId-$AwsRegion"
    $key = "conexao-solidaria/$Environment/aws.tfstate"

    $bucketExists = (Invoke-Checked {
        aws s3api list-buckets `
            --profile $AwsProfile `
            --query "contains(Buckets[].Name, '$bucket')" `
            --output text
    } "Nao foi possivel consultar os buckets S3 da conta." | Out-String).Trim()
    if ($bucketExists -ne "True") {
        if ($AwsRegion -eq "us-east-1") {
            $null = Invoke-Checked {
                aws s3api create-bucket --bucket $bucket --region $AwsRegion --profile $AwsProfile
            } "Nao foi possivel criar o bucket S3 do estado Terraform."
        }
        else {
            $null = Invoke-Checked {
                aws s3api create-bucket `
                    --bucket $bucket `
                    --region $AwsRegion `
                    --create-bucket-configuration "LocationConstraint=$AwsRegion" `
                    --profile $AwsProfile
            } "Nao foi possivel criar o bucket S3 do estado Terraform."
        }
    }

    $null = Invoke-Checked {
        aws s3api put-public-access-block `
            --bucket $bucket `
            --public-access-block-configuration `
                "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true" `
            --profile $AwsProfile
    } "Nao foi possivel bloquear acesso publico ao bucket."
    $null = Invoke-Checked {
        aws s3api put-bucket-versioning `
            --bucket $bucket `
            --versioning-configuration Status=Enabled `
            --profile $AwsProfile
    } "Nao foi possivel habilitar versionamento no bucket."
    $encryptionPath = Join-Path $stateDirectory "aws-$Environment-encryption.json"
    $encryption = @{
        Rules = @(
            @{
                ApplyServerSideEncryptionByDefault = @{ SSEAlgorithm = "AES256" }
                BucketKeyEnabled                   = $true
            }
        )
    } | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($encryptionPath, $encryption, [System.Text.UTF8Encoding]::new($false))
    $null = Invoke-Checked {
        aws s3api put-bucket-encryption `
            --bucket $bucket `
            --server-side-encryption-configuration "file://$($encryptionPath.Replace('\', '/'))" `
            --profile $AwsProfile
    } "Nao foi possivel habilitar criptografia no bucket."
    $null = Invoke-Checked {
        aws s3api put-bucket-ownership-controls `
            --bucket $bucket `
            --ownership-controls "Rules=[{ObjectOwnership=BucketOwnerEnforced}]" `
            --profile $AwsProfile
    } "Nao foi possivel definir propriedade dos objetos do bucket."
    $null = Invoke-Checked {
        aws s3api put-bucket-tagging `
            --bucket $bucket `
            --tagging "TagSet=[{Key=project,Value=conexao-solidaria},{Key=purpose,Value=terraform-state},{Key=managed-by,Value=bootstrap}]" `
            --profile $AwsProfile
    } "Nao foi possivel aplicar tags ao bucket."

    $policyPath = Join-Path $stateDirectory "aws-$Environment-policy.json"
    $policy = @{
        Version = "2012-10-17"
        Statement = @(
            @{
                Sid = "DenyInsecureTransport"
                Effect = "Deny"
                Principal = "*"
                Action = "s3:*"
                Resource = @("arn:aws:s3:::$bucket", "arn:aws:s3:::$bucket/*")
                Condition = @{ Bool = @{ "aws:SecureTransport" = "false" } }
            }
        )
    } | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($policyPath, $policy, [System.Text.UTF8Encoding]::new($false))
    $null = Invoke-Checked {
        aws s3api put-bucket-policy `
            --bucket $bucket `
            --policy "file://$($policyPath.Replace('\', '/'))" `
            --profile $AwsProfile
    } "Nao foi possivel aplicar a politica TLS ao bucket."

    $backendPath = Join-Path $stateDirectory "aws-$Environment.hcl"
    $backend = @"
bucket       = "$bucket"
key          = "$key"
region       = "$AwsRegion"
encrypt      = true
use_lockfile = true
profile      = "$AwsProfile"
"@
    [System.IO.File]::WriteAllText($backendPath, $backend, [System.Text.UTF8Encoding]::new($false))
    Initialize-Terraform "infra/terraform/aws" $backendPath

    Write-Host "Backend AWS inicializado: s3://$bucket/$key" -ForegroundColor Green
}

if ($Provider -eq "azure") {
    Initialize-AzureBackend
}
else {
    Initialize-AwsBackend
}
