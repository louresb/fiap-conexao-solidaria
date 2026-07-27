param(
    [string]$TerraformDirectory = "infra/terraform/azure",
    [string]$ImageTag = "latest",
    [string]$AiEndpoint,
    [string]$AiModel,
    [ValidateSet("api-key", "Authorization")]
    [string]$AiApiKeyHeader = "api-key",
    [string]$AiApiKey,
    [switch]$SkipSecretBootstrap,
    [switch]$SkipObservability
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$terraformPath = Join-Path $root $TerraformDirectory
$namespace = "conexao-solidaria"
$stateDirectory = Join-Path $root ".local/azure"
Set-Location $root

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    $result = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
    return $result
}

function Apply-Generated([string[]]$Yaml, [string]$FailureMessage) {
    $Yaml | kubectl apply -n $namespace -f -
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Apply-FileConfigMap([string]$Name, [string]$FromFile) {
    $yaml = kubectl create configmap $Name `
        -n $namespace `
        --from-file=$FromFile `
        --dry-run=client `
        -o yaml
    Apply-Generated $yaml "Nao foi possivel aplicar o ConfigMap $Name."
}

$null = Invoke-Checked { az account show --output none --only-show-errors } `
    "Azure CLI nao esta autenticada. Execute 'az login' antes do deploy."
$null = Invoke-Checked { helm version --short } "Helm 3 nao esta disponivel."
$null = Invoke-Checked { kubectl version --client=true } "kubectl nao esta disponivel."

if (-not (Get-Command kubelogin -ErrorAction SilentlyContinue)) {
    $kubeloginDirectory = Join-Path $env:USERPROFILE ".azure-kubelogin"
    $kubeloginExecutable = Join-Path $kubeloginDirectory "kubelogin.exe"
    if (Test-Path $kubeloginExecutable) {
        $env:PATH = "$kubeloginDirectory;$env:PATH"
    }
}
if (-not (Get-Command kubelogin -ErrorAction SilentlyContinue)) {
    throw "kubelogin nao esta disponivel. Execute 'az aks install-cli' antes do deploy."
}

$aiEnabled = -not [string]::IsNullOrWhiteSpace($AiEndpoint) -or
    -not [string]::IsNullOrWhiteSpace($AiModel) -or
    -not [string]::IsNullOrWhiteSpace($AiApiKey)
if ($aiEnabled) {
    $aiUri = $null
    if ([string]::IsNullOrWhiteSpace($AiEndpoint) -or
        -not [Uri]::TryCreate($AiEndpoint, [UriKind]::Absolute, [ref]$aiUri) -or
        $aiUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "AiEndpoint deve ser um endpoint HTTPS absoluto."
    }
    if ([string]::IsNullOrWhiteSpace($AiModel)) {
        throw "AiModel e obrigatorio quando a geracao fundamentada esta habilitada."
    }
    if (-not $SkipSecretBootstrap -and [string]::IsNullOrWhiteSpace($AiApiKey)) {
        throw "Informe AiApiKey ou use SkipSecretBootstrap quando o segredo ja existir no Key Vault."
    }
}

$outputs = (Invoke-Checked {
    terraform "-chdir=$terraformPath" output -json
} "Nao foi possivel ler os outputs da Azure. Aplique o Terraform primeiro.") | Out-String | ConvertFrom-Json

$resourceGroup = $outputs.resource_group_name.value
$clusterName = $outputs.cluster_name.value
$registryName = $outputs.container_registry_name.value
$keyVaultName = $outputs.key_vault_name.value
$csiClientId = $outputs.key_vault_csi_client_id.value
$tenantId = $outputs.github_azure_tenant_id.value
$hostname = $outputs.application_hostname.value
$publicIpName = $outputs.ingress_public_ip_name.value
$publicAppUrl = "https://$hostname"

if (-not $SkipSecretBootstrap) {
    & "$PSScriptRoot/Initialize-AzureSecrets.ps1" `
        -TerraformDirectory $TerraformDirectory `
        -AiApiKey $AiApiKey
}

$null = Invoke-Checked {
    az aks get-credentials `
        --resource-group $resourceGroup `
        --name $clusterName `
        --overwrite-existing `
        --output none `
        --only-show-errors
} "Nao foi possivel configurar o acesso ao AKS."
$null = Invoke-Checked {
    kubelogin convert-kubeconfig -l azurecli
} "Nao foi possivel preparar a autenticacao do kubectl no AKS."

$namespaceYaml = kubectl create namespace $namespace --dry-run=client -o yaml
$namespaceYaml | kubectl apply -f -
if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel criar o namespace $namespace."
}

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$secretProviderTemplate = Get-Content "deploy/kubernetes/cloud/azure-secret-provider.template.yaml" -Raw -Encoding UTF8
$secretProviderYaml = $secretProviderTemplate.Replace("__KEY_VAULT_NAME__", $keyVaultName)
$secretProviderYaml = $secretProviderYaml.Replace("__CSI_CLIENT_ID__", $csiClientId)
$secretProviderYaml = $secretProviderYaml.Replace("__TENANT_ID__", $tenantId)
$secretProviderPath = Join-Path $stateDirectory "azure-secret-provider.yaml"
[System.IO.File]::WriteAllText($secretProviderPath, $secretProviderYaml, [System.Text.UTF8Encoding]::new($false))
$null = Invoke-Checked { kubectl apply -f $secretProviderPath } `
    "Nao foi possivel configurar a sincronizacao do Azure Key Vault."
$null = Invoke-Checked {
    kubectl rollout status deployment/conexao-solidaria-secret-sync -n $namespace --timeout=5m
} "O sincronizador do Azure Key Vault nao ficou pronto."
$null = Invoke-Checked {
    kubectl get secret conexao-solidaria-runtime -n $namespace -o name
} "O segredo de runtime nao foi sincronizado pelo CSI driver."

$cloudSettings = kubectl create configmap conexao-solidaria-cloud `
    -n $namespace `
    --from-literal="hostname=$hostname" `
    --from-literal="public-app-url=$publicAppUrl" `
    --dry-run=client `
    -o yaml
Apply-Generated $cloudSettings "Nao foi possivel aplicar as configuracoes publicas da Azure."

Apply-FileConfigMap "conexao-solidaria-postgres-init" "001-databases.sql=infra/postgres/init/001-databases.sql"
Apply-FileConfigMap "conexao-solidaria-rabbitmq-config" "enabled_plugins=infra/rabbitmq/enabled_plugins"
Apply-FileConfigMap "conexao-solidaria-keycloak-realm" "conexao-solidaria-realm.json=infra/keycloak/conexao-solidaria-realm.json"
Apply-FileConfigMap "conexao-solidaria-keycloak-seed" "seed-users.sh=infra/keycloak/seed-users.sh"
Apply-FileConfigMap "conexao-solidaria-keycloak-profile" "user-profile.json=infra/keycloak/user-profile.json"
$themeYaml = kubectl create configmap conexao-solidaria-keycloak-theme `
    -n $namespace `
    --from-file="theme.properties=infra/keycloak/themes/conexao-solidaria/login/theme.properties" `
    --from-file="login.css=infra/keycloak/themes/conexao-solidaria/login/resources/css/login.css" `
    --from-file="messages_pt_BR.properties=infra/keycloak/themes/conexao-solidaria/login/messages/messages_pt_BR.properties" `
    --dry-run=client `
    -o yaml
Apply-Generated $themeYaml "Nao foi possivel aplicar o tema do Keycloak."

$cloudRuntimeYaml = Invoke-Checked {
    kubectl kustomize deploy/kubernetes/cloud/runtime --load-restrictor LoadRestrictionsNone
} "Nao foi possivel renderizar os servicos de dados e mensageria."
Apply-Generated $cloudRuntimeYaml "Nao foi possivel aplicar os servicos de dados e mensageria."
$null = Invoke-Checked {
    kubectl set env deployment/conexao-solidaria-keycloak `
        -n $namespace `
        "KC_HOSTNAME=$publicAppUrl/auth" `
        "KC_HTTP_RELATIVE_PATH=/auth" `
        "KC_HTTP_MANAGEMENT_RELATIVE_PATH=/" `
        "KC_PROXY_HEADERS=xforwarded"
} "Nao foi possivel configurar o hostname publico do Keycloak."

$infrastructureWorkloads = @(
    "statefulset/conexao-solidaria-postgres",
    "deployment/conexao-solidaria-redis",
    "deployment/conexao-solidaria-rabbitmq",
    "statefulset/conexao-solidaria-mongodb",
    "statefulset/conexao-solidaria-opensearch",
    "deployment/conexao-solidaria-keycloak"
)
foreach ($workload in $infrastructureWorkloads) {
    $null = Invoke-Checked { kubectl rollout status $workload -n $namespace --timeout=8m } `
        "$workload nao ficou pronto."
}

kubectl delete job conexao-solidaria-keycloak-seed -n $namespace --ignore-not-found=true --wait=true
$null = Invoke-Checked {
    kubectl apply -n $namespace -f deploy/kubernetes/cloud/keycloak-seed-job.yaml
} "Nao foi possivel criar o bootstrap do Keycloak."
$null = Invoke-Checked {
    kubectl wait -n $namespace --for=condition=complete job/conexao-solidaria-keycloak-seed --timeout=8m
} "O bootstrap do Keycloak nao foi concluido."

$certManagerCrd = Invoke-Checked {
    kubectl get crd certificates.cert-manager.io --ignore-not-found -o name
} "Nao foi possivel verificar a instalacao do cert-manager."
if ([string]::IsNullOrWhiteSpace(($certManagerCrd | Out-String))) {
    $null = Invoke-Checked {
        kubectl apply --server-side -f https://github.com/cert-manager/cert-manager/releases/download/v1.21.0/cert-manager.yaml
    } "Nao foi possivel instalar o cert-manager."
}
else {
    Write-Host "cert-manager ja instalado; preservando campos gerenciados pelo AKS."
}
foreach ($deployment in @("cert-manager", "cert-manager-cainjector", "cert-manager-webhook")) {
    $null = Invoke-Checked {
        kubectl rollout status "deployment/$deployment" -n cert-manager --timeout=5m
    } "O componente $deployment do cert-manager nao ficou pronto."
}

if (-not $SkipObservability) {
    Apply-FileConfigMap "conexao-solidaria-prometheus-config" "prometheus.yml=deploy/kubernetes/local/prometheus.yml"
    Apply-FileConfigMap "conexao-solidaria-otel-collector-config" "config.yml=infra/opentelemetry/collector-config.yml"
    Apply-FileConfigMap "conexao-solidaria-tempo-config" "tempo.yml=infra/opentelemetry/tempo.yml"
    Apply-FileConfigMap "conexao-solidaria-grafana-datasources" "datasources.yml=deploy/kubernetes/local/grafana-datasources.yml"
    Apply-FileConfigMap "conexao-solidaria-grafana-dashboard-provider" "dashboards.yml=infra/grafana/provisioning/dashboards/dashboards.yml"
    Apply-FileConfigMap "conexao-solidaria-grafana-dashboard" "conexao-solidaria.json=infra/grafana/provisioning/dashboards/conexao-solidaria.json"

    $cloudObservabilityYaml = Invoke-Checked {
        kubectl kustomize deploy/kubernetes/cloud/observability --load-restrictor LoadRestrictionsNone
    } "Falha ao renderizar observabilidade."
    Apply-Generated $cloudObservabilityYaml "Falha ao aplicar observabilidade."
}

$helmArguments = @(
    "upgrade", "--install", "conexao-solidaria", "deploy/helm/conexao-solidaria",
    "--namespace", $namespace,
    "--create-namespace",
    "--values", "deploy/helm/conexao-solidaria/values.azure.yaml",
    "--set-string", "image.registry=$registryName.azurecr.io",
    "--set-string", "image.tag=$ImageTag",
    "--set-string", "ingress.host=$hostname",
    "--set-string", "azureRouting.publicIpName=$publicIpName",
    "--set-string", "azureRouting.publicIpResourceGroup=$resourceGroup",
    "--wait",
    "--atomic",
    "--timeout", "10m"
)
$helmUpgradeHelp = helm upgrade --help | Out-String
if ($helmUpgradeHelp -match "--force-conflicts") {
    $helmArguments += "--force-conflicts"
}
if ($aiEnabled) {
    $helmArguments += @(
        "--set-string", "components.knowledge-api.env.AI__Enabled=true",
        "--set-string", "components.knowledge-api.env.AI__Endpoint=$AiEndpoint",
        "--set-string", "components.knowledge-api.env.AI__Model=$AiModel",
        "--set-string", "components.knowledge-api.env.AI__ApiKeyHeader=$AiApiKeyHeader"
    )
}

$null = Invoke-Checked { helm @helmArguments } "Nao foi possivel publicar a aplicacao no AKS."

$applicationWorkloads = @(
    "audit-api", "campaigns-api", "campaigns-worker", "donations-api", "gateway",
    "identity-api", "knowledge-api", "payments-api", "web"
)
foreach ($component in $applicationWorkloads) {
    $null = Invoke-Checked {
        kubectl rollout status "deployment/conexao-solidaria-$component" -n $namespace --timeout=10m
    } "$component nao ficou pronto."
}

$null = Invoke-Checked {
    kubectl wait certificate/conexao-solidaria-tls -n $namespace --for=condition=Ready --timeout=8m
} "O certificado TLS nao ficou pronto. Verifique DNS e o challenge ACME."
$null = Invoke-Checked {
    curl.exe --fail --silent --show-error --retry 12 --retry-delay 10 "$publicAppUrl/health/ready"
} "O endpoint publico nao passou no readiness check."
$null = Invoke-Checked {
    curl.exe --fail --silent --show-error --retry 6 --retry-delay 5 `
        "$publicAppUrl/auth/realms/conexao-solidaria/.well-known/openid-configuration"
} "O discovery OIDC publico do Keycloak nao esta acessivel."

Write-Host ""
Write-Host "Conexao Solidaria publicada na Azure." -ForegroundColor Green
Write-Host "Produto:  $publicAppUrl"
Write-Host "Keycloak: $publicAppUrl/auth"
Write-Host "Cluster:  $clusterName"
Write-Host "Registry: $registryName.azurecr.io"
