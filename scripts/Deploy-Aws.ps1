param(
    [string]$TerraformDirectory = "infra/terraform/aws",
    [string]$AwsProfile = "conexao-solidaria",
    [string]$ImageTag = "latest",
    [string]$AppHost,
    [string]$AiEndpoint,
    [string]$AiModel,
    [ValidateSet("api-key", "Authorization")]
    [string]$AiApiKeyHeader = "api-key",
    [ValidateSet("max_tokens", "max_completion_tokens")]
    [string]$AiTokenLimitParameter = "max_tokens",
    [string]$AiApiKey,
    [switch]$SkipSecretBootstrap,
    [switch]$SkipObservability
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$terraformPath = Join-Path $root $TerraformDirectory
$namespace = "conexao-solidaria"
$stateDirectory = Join-Path $root ".local/aws"
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

function Ensure-RuntimeSecretKey([string]$Key) {
    $encoded = kubectl get secret conexao-solidaria-runtime `
        -n $namespace `
        -o "jsonpath={.data.$Key}" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($encoded | Out-String))) {
        return
    }

    $null = Invoke-Checked {
        kubectl delete secret conexao-solidaria-runtime -n $namespace --ignore-not-found=true --wait=true
    } "Nao foi possivel atualizar o segredo de runtime."
    $null = Invoke-Checked {
        kubectl delete pod `
            -n $namespace `
            -l app.kubernetes.io/name=secret-sync `
            --wait=true
    } "Nao foi possivel reiniciar o sincronizador de segredos."
    $null = Invoke-Checked {
        kubectl rollout status deployment/conexao-solidaria-secret-sync -n $namespace --timeout=5m
    } "O sincronizador de segredos nao ficou pronto."

    for ($attempt = 1; $attempt -le 45; $attempt++) {
        $encoded = kubectl get secret conexao-solidaria-runtime `
            -n $namespace `
            -o "jsonpath={.data.$Key}" 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($encoded | Out-String))) {
            return
        }
        Start-Sleep -Seconds 2
    }

    throw "O segredo de runtime nao contem a chave '$Key'."
}

function Resolve-IPv4([string]$Name) {
    $addresses = [System.Net.Dns]::GetHostAddresses($Name) |
        Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork }
    return @($addresses | ForEach-Object { $_.IPAddressToString })
}

$null = Invoke-Checked { aws sts get-caller-identity --profile $AwsProfile --output json } `
    "AWS CLI nao esta autenticada no profile '$AwsProfile'. Execute 'aws login --profile $AwsProfile'."
$null = Invoke-Checked { helm version --short } "Helm 3 nao esta disponivel."
$null = Invoke-Checked { kubectl version --client=true } "kubectl nao esta disponivel."

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
        throw "Informe AiApiKey ou use SkipSecretBootstrap quando o segredo ja existir no Secrets Manager."
    }
}

$outputs = (Invoke-Checked {
    terraform "-chdir=$terraformPath" output -json
} "Nao foi possivel ler os outputs da AWS. Aplique o Terraform com enable_eks=true primeiro.") |
    Out-String |
    ConvertFrom-Json

$clusterName = $outputs.cluster_name.value
$region = $outputs.aws_region.value
$registry = $outputs.ecr_registry.value
$runtimeSecretArn = $outputs.runtime_secret_arn.value
if ([string]::IsNullOrWhiteSpace($clusterName) -or [string]::IsNullOrWhiteSpace($runtimeSecretArn)) {
    throw "O Terraform AWS foi aplicado sem EKS. Habilite enable_eks e confirme acknowledge_eks_costs para esta janela de demonstracao."
}

if (-not $SkipSecretBootstrap) {
    & "$PSScriptRoot/Initialize-AwsSecrets.ps1" `
        -TerraformDirectory $TerraformDirectory `
        -AwsProfile $AwsProfile `
        -AiApiKey $AiApiKey
}

$null = Invoke-Checked {
    aws eks update-kubeconfig `
        --name $clusterName `
        --region $region `
        --profile $AwsProfile
} "Nao foi possivel configurar o acesso ao EKS."

$namespaceYaml = kubectl create namespace $namespace --dry-run=client -o yaml
$namespaceYaml | kubectl apply -f -
if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel criar o namespace $namespace."
}
$null = Invoke-Checked {
    kubectl apply -f "deploy/kubernetes/cloud/aws-storage-class.yaml"
} "Nao foi possivel configurar a StorageClass gp3 do EBS CSI."

$null = Invoke-Checked {
    helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx --force-update
} "Nao foi possivel adicionar o repositorio ingress-nginx."
$null = Invoke-Checked {
    helm upgrade --install ingress-nginx ingress-nginx/ingress-nginx `
        --namespace ingress-nginx `
        --create-namespace `
        --set-string 'controller.service.annotations.service\.beta\.kubernetes\.io/aws-load-balancer-type=nlb' `
        --set-string 'controller.ingressClassResource.name=nginx' `
        --set-string 'controller.ingressClass=nginx' `
        --wait `
        --timeout 10m
} "Nao foi possivel instalar o ingress-nginx no EKS."

$loadBalancerHostname = $null
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $loadBalancerHostname = (& kubectl get service ingress-nginx-controller `
        -n ingress-nginx `
        -o 'jsonpath={.status.loadBalancer.ingress[0].hostname}' 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($loadBalancerHostname)) {
        break
    }
    Start-Sleep -Seconds 10
}
if ([string]::IsNullOrWhiteSpace($loadBalancerHostname)) {
    throw "O Network Load Balancer nao recebeu um hostname publico."
}

$loadBalancerAddresses = @()
for ($attempt = 0; $attempt -lt 30 -and $loadBalancerAddresses.Count -eq 0; $attempt++) {
    try {
        $loadBalancerAddresses = @(Resolve-IPv4 $loadBalancerHostname)
    }
    catch {
        Start-Sleep -Seconds 10
    }
}
if ($loadBalancerAddresses.Count -eq 0) {
    throw "O hostname do Network Load Balancer ainda nao possui enderecos IPv4."
}

if ([string]::IsNullOrWhiteSpace($AppHost)) {
    $existingAppHost = (& kubectl get ingress conexao-solidaria `
        -n $namespace `
        -o 'jsonpath={.spec.rules[0].host}' 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($existingAppHost)) {
        $AppHost = $existingAppHost
    }
}

if ([string]::IsNullOrWhiteSpace($AppHost)) {
    $AppHost = "$($loadBalancerAddresses[0]).sslip.io"
}
else {
    $resolvedApplicationAddresses = @()
    try {
        $resolvedApplicationAddresses = @(Resolve-IPv4 $AppHost)
    }
    catch {
        $resolvedApplicationAddresses = @()
    }
    if (@($resolvedApplicationAddresses | Where-Object { $loadBalancerAddresses -contains $_ }).Count -eq 0) {
        throw "Configure '$AppHost' como CNAME de '$loadBalancerHostname' e execute o script novamente."
    }
}

$null = Invoke-Checked {
    helm repo add secrets-store-csi-driver https://kubernetes-sigs.github.io/secrets-store-csi-driver/charts --force-update
} "Nao foi possivel adicionar o repositorio Secrets Store CSI."
$null = Invoke-Checked {
    helm upgrade --install csi-secrets-store secrets-store-csi-driver/secrets-store-csi-driver `
        --namespace kube-system `
        --set syncSecret.enabled=true `
        --set enableSecretRotation=true `
        --set-string 'tokenRequests[0].audience=sts.amazonaws.com' `
        --set-string 'tokenRequests[1].audience=pods.eks.amazonaws.com' `
        --wait `
        --timeout 5m
} "Nao foi possivel instalar o Secrets Store CSI Driver."
$null = Invoke-Checked {
    helm repo add aws-secrets-manager https://aws.github.io/secrets-store-csi-driver-provider-aws --force-update
} "Nao foi possivel adicionar o repositorio ASCP."
$null = Invoke-Checked {
    helm upgrade --install secrets-provider-aws aws-secrets-manager/secrets-store-csi-driver-provider-aws `
        --namespace kube-system `
        --set secrets-store-csi-driver.install=false `
        --wait `
        --timeout 5m
} "Nao foi possivel instalar o provider AWS do Secrets Store CSI."

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$secretProviderTemplate = Get-Content "deploy/kubernetes/cloud/aws-secret-provider.template.yaml" -Raw -Encoding UTF8
$secretProviderYaml = $secretProviderTemplate.Replace("__RUNTIME_SECRET_ARN__", $runtimeSecretArn)
$secretProviderYaml = $secretProviderYaml.Replace("__AWS_REGION__", $region)
$secretProviderPath = Join-Path $stateDirectory "aws-secret-provider.yaml"
[System.IO.File]::WriteAllText($secretProviderPath, $secretProviderYaml, [System.Text.UTF8Encoding]::new($false))
$null = Invoke-Checked { kubectl apply -f $secretProviderPath } `
    "Nao foi possivel configurar a sincronizacao do AWS Secrets Manager."
$null = Invoke-Checked {
    kubectl rollout status deployment/conexao-solidaria-secret-sync -n $namespace --timeout=5m
} "O sincronizador do AWS Secrets Manager nao ficou pronto."
$null = Invoke-Checked {
    kubectl get secret conexao-solidaria-runtime -n $namespace -o name
} "O segredo de runtime nao foi sincronizado pelo CSI driver."
Ensure-RuntimeSecretKey "zabbix-admin-password"

$publicAppUrl = "https://$AppHost"
$cloudSettings = kubectl create configmap conexao-solidaria-cloud `
    -n $namespace `
    --from-literal="hostname=$AppHost" `
    --from-literal="public-app-url=$publicAppUrl" `
    --dry-run=client `
    -o yaml
Apply-Generated $cloudSettings "Nao foi possivel aplicar as configuracoes publicas da AWS."

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
    Write-Host "cert-manager ja instalado; preservando campos gerenciados pelo cluster."
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
    Apply-FileConfigMap "conexao-solidaria-zabbix-bootstrap" "Initialize-Zabbix.ps1=scripts/Initialize-Zabbix.ps1"

    $null = Invoke-Checked {
        kubectl delete job/conexao-solidaria-zabbix-bootstrap -n $namespace --ignore-not-found=true
    } "Nao foi possivel preparar o bootstrap do Zabbix."

    $cloudObservabilityYaml = Invoke-Checked {
        kubectl kustomize deploy/kubernetes/cloud/observability --load-restrictor LoadRestrictionsNone
    } "Falha ao renderizar observabilidade."
    Apply-Generated $cloudObservabilityYaml "Falha ao aplicar observabilidade."
}

$helmArguments = @(
    "upgrade", "--install", "conexao-solidaria", "deploy/helm/conexao-solidaria",
    "--namespace", $namespace,
    "--values", "deploy/helm/conexao-solidaria/values.aws.yaml",
    "--set-string", "image.registry=$registry",
    "--set-string", "image.tag=$ImageTag",
    "--set-string", "ingress.host=$AppHost",
    "--wait",
    "--timeout", "10m"
)
$helmUpgradeHelp = helm upgrade --help | Out-String
if ($helmUpgradeHelp -match "--rollback-on-failure") {
    $helmArguments += "--rollback-on-failure"
}
else {
    $helmArguments += "--atomic"
}
if ($helmUpgradeHelp -match "--force-conflicts" -and $helmUpgradeHelp -match "--server-side") {
    $helmArguments += @("--server-side=true", "--force-conflicts")
}
if ($aiEnabled) {
    $helmArguments += @(
        "--set-string", "components.knowledge-api.env.AI__Enabled=true",
        "--set-string", "components.knowledge-api.env.AI__Endpoint=$AiEndpoint",
        "--set-string", "components.knowledge-api.env.AI__Model=$AiModel",
        "--set-string", "components.knowledge-api.env.AI__ApiKeyHeader=$AiApiKeyHeader",
        "--set-string", "components.knowledge-api.env.AI__TokenLimitParameter=$AiTokenLimitParameter"
    )
}

$null = Invoke-Checked {
    helm @helmArguments
} "Nao foi possivel publicar a aplicacao no EKS."

if (-not $SkipObservability) {
    try {
        $null = Invoke-Checked {
            kubectl wait job/conexao-solidaria-zabbix-bootstrap `
                -n $namespace `
                --for=condition=Complete `
                --timeout=7m
        } "O bootstrap do Zabbix nao foi concluido."
    }
    catch {
        kubectl logs job/conexao-solidaria-zabbix-bootstrap -n $namespace --all-containers=true
        throw
    }
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
Write-Host "Conexao Solidaria publicada na AWS." -ForegroundColor Green
Write-Host "Produto:       $publicAppUrl"
Write-Host "Keycloak:      $publicAppUrl/auth"
Write-Host "Cluster:       $clusterName"
Write-Host "Registry:      $registry"
Write-Host "Load Balancer: $loadBalancerHostname"
