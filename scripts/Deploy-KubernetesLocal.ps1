param(
    [switch]$SkipBuild,
    [switch]$Reset,
    [switch]$Reseed
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$namespace = "conexao-solidaria"
$stateDirectory = Join-Path $root ".local"
$portForwardState = Join-Path $stateDirectory "kubernetes-port-forwards.json"
$imageTagState = Join-Path $stateDirectory "kubernetes-image-tag.txt"
$applicationWorkloads = @(
    "audit-api",
    "campaigns-api",
    "donations-worker",
    "gateway",
    "identity-api",
    "knowledge-api",
    "payments-api",
    "web"
)
Set-Location $root

function Invoke-Checked([scriptblock]$Command, [string]$FailureMessage) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
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

function Stop-PortForwards {
    if (-not (Test-Path $portForwardState)) {
        return
    }

    $processIds = Get-Content $portForwardState -Raw | ConvertFrom-Json
    foreach ($processId in @($processIds)) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    Remove-Item $portForwardState -Force -ErrorAction SilentlyContinue
}

function Start-PortForwards {
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    Stop-PortForwards

    $forwards = @(
        @{ Service = "conexao-solidaria-web"; Local = 31080; Remote = 80 },
        @{ Service = "conexao-solidaria-gateway"; Local = 31081; Remote = 8080 },
        @{ Service = "conexao-solidaria-keycloak"; Local = 31082; Remote = 8080 },
        @{ Service = "conexao-solidaria-rabbitmq"; Local = 32672; Remote = 15672 },
        @{ Service = "conexao-solidaria-grafana"; Local = 31090; Remote = 3000 },
        @{ Service = "conexao-solidaria-prometheus"; Local = 31091; Remote = 9090 },
        @{ Service = "conexao-solidaria-zabbix-web"; Local = 31092; Remote = 8080 },
        @{ Service = "conexao-solidaria-identity-api"; Local = 31101; Remote = 8080 },
        @{ Service = "conexao-solidaria-campaigns-api"; Local = 31102; Remote = 8080 },
        @{ Service = "conexao-solidaria-payments-api"; Local = 31103; Remote = 8080 },
        @{ Service = "conexao-solidaria-audit-api"; Local = 31104; Remote = 8080 },
        @{ Service = "conexao-solidaria-knowledge-api"; Local = 31105; Remote = 8080 }
    )
    $processIds = @()

    foreach ($forward in $forwards) {
        $logPrefix = Join-Path $stateDirectory "port-forward-$($forward.Local)"
        $process = Start-Process `
            -FilePath "kubectl" `
            -ArgumentList @(
                "port-forward",
                "-n", $namespace,
                "service/$($forward.Service)",
                "$($forward.Local):$($forward.Remote)",
                "--address=127.0.0.1") `
            -WindowStyle Hidden `
            -RedirectStandardOutput "$logPrefix.out.log" `
            -RedirectStandardError "$logPrefix.err.log" `
            -PassThru
        $processIds += $process.Id
    }

    [System.IO.File]::WriteAllText(
        $portForwardState,
        ($processIds | ConvertTo-Json),
        [System.Text.UTF8Encoding]::new($false))

    foreach ($forward in $forwards) {
        $available = $false
        for ($attempt = 1; $attempt -le 30 -and -not $available; $attempt++) {
            try {
                $client = [System.Net.Sockets.TcpClient]::new()
                $client.Connect("127.0.0.1", $forward.Local)
                $client.Dispose()
                $available = $true
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $available) {
            throw "O port-forward local $($forward.Local) nao ficou disponivel."
        }
    }
}

if ((kubectl config current-context) -ne "docker-desktop") {
    throw "O contexto atual nao e docker-desktop. Altere-o antes de executar o ambiente local."
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop nao esta disponivel."
}

if (-not (Test-Path ".env")) {
    & "$PSScriptRoot\Initialize-LocalEnvironment.ps1"
}
else {
    & "$PSScriptRoot\Initialize-LocalEnvironment.ps1"
}

$environment = Get-Content ".env" -Raw | ConvertFrom-StringData

if ($Reset) {
    Stop-PortForwards
    kubectl delete namespace $namespace --ignore-not-found=true --wait=true
}

docker compose --profile tools --profile ops down --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel encerrar o ambiente Docker Compose."
}

if (-not $SkipBuild) {
    Invoke-Checked { docker compose build } "O build das imagens locais falhou."

    $imageTag = "local-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
    foreach ($component in $applicationWorkloads) {
        Invoke-Checked {
            docker tag "conexao-solidaria-${component}:latest" "conexao-solidaria-${component}:$imageTag"
        } "Nao foi possivel versionar a imagem local de $component."
    }

    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText($imageTagState, $imageTag, [System.Text.UTF8Encoding]::new($false))
}
elseif (Test-Path $imageTagState) {
    $imageTag = (Get-Content $imageTagState -Raw).Trim()
}
else {
    $imageTag = "latest"
}

Invoke-Checked { kubectl apply -f deploy/kubernetes/local/namespace.yaml } "Nao foi possivel criar o namespace local."

$secretYaml = kubectl create secret generic conexao-solidaria-runtime `
    -n $namespace `
    --from-literal="postgres-password=$($environment.POSTGRES_PASSWORD)" `
    --from-literal="rabbitmq-password=$($environment.RABBITMQ_PASSWORD)" `
    --from-literal="redis-password=$($environment.REDIS_PASSWORD)" `
    --from-literal="mongo-password=$($environment.MONGO_PASSWORD)" `
    --from-literal="keycloak-admin-password=$($environment.KEYCLOAK_ADMIN_PASSWORD)" `
    --from-literal="keycloak-client-secret=$($environment.KEYCLOAK_CLIENT_SECRET)" `
    --from-literal="grafana-admin-password=$($environment.GRAFANA_ADMIN_PASSWORD)" `
    --from-literal="zabbix-db-password=$($environment.ZABBIX_DB_PASSWORD)" `
    --from-literal="payment-webhook-secret=$($environment.PAYMENT_WEBHOOK_SECRET)" `
    --from-literal="demo-manager-password=$($environment.DEMO_MANAGER_PASSWORD)" `
    --from-literal="demo-donor-password=$($environment.DEMO_DONOR_PASSWORD)" `
    --from-literal="identity-db-connection=Host=conexao-solidaria-postgres;Port=5432;Database=conexao_identity;Username=conexao;Password=$($environment.POSTGRES_PASSWORD)" `
    --from-literal="campaigns-db-connection=Host=conexao-solidaria-postgres;Port=5432;Database=conexao_campaigns;Username=conexao;Password=$($environment.POSTGRES_PASSWORD)" `
    --from-literal="payments-db-connection=Host=conexao-solidaria-postgres;Port=5432;Database=conexao_payments;Username=conexao;Password=$($environment.POSTGRES_PASSWORD)" `
    --from-literal="redis-connection=conexao-solidaria-redis:6379,password=$($environment.REDIS_PASSWORD),abortConnect=false" `
    --from-literal="mongo-connection=mongodb://conexao:$($environment.MONGO_PASSWORD)@conexao-solidaria-mongodb:27017/?authSource=admin" `
    --dry-run=client `
    -o yaml
Apply-Generated $secretYaml "Nao foi possivel aplicar os segredos locais."

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
Apply-FileConfigMap "conexao-solidaria-prometheus-config" "prometheus.yml=deploy/kubernetes/local/prometheus.yml"
Apply-FileConfigMap "conexao-solidaria-grafana-datasources" "datasources.yml=deploy/kubernetes/local/grafana-datasources.yml"
Apply-FileConfigMap "conexao-solidaria-grafana-dashboard-provider" "dashboards.yml=infra/grafana/provisioning/dashboards/dashboards.yml"
Apply-FileConfigMap "conexao-solidaria-grafana-dashboard" "conexao-solidaria.json=infra/grafana/provisioning/dashboards/conexao-solidaria.json"

Invoke-Checked { kubectl apply -n $namespace -f deploy/kubernetes/local/infrastructure.yaml } "Falha ao aplicar a infraestrutura local."
Invoke-Checked { kubectl apply -n $namespace -f deploy/kubernetes/local/monitoring.yaml } "Falha ao aplicar a observabilidade local."
Invoke-Checked { kubectl apply -n $namespace -f deploy/kubernetes/local/zabbix.yaml } "Falha ao aplicar o Zabbix local."

$infrastructureWorkloads = @(
    "statefulset/conexao-solidaria-postgres",
    "deployment/conexao-solidaria-redis",
    "deployment/conexao-solidaria-rabbitmq",
    "statefulset/conexao-solidaria-mongodb",
    "statefulset/conexao-solidaria-opensearch",
    "deployment/conexao-solidaria-keycloak",
    "deployment/conexao-solidaria-loki",
    "deployment/conexao-solidaria-prometheus",
    "deployment/conexao-solidaria-grafana",
    "statefulset/conexao-solidaria-zabbix-db",
    "deployment/conexao-solidaria-zabbix-server",
    "deployment/conexao-solidaria-zabbix-web"
)

foreach ($workload in $infrastructureWorkloads) {
    Invoke-Checked { kubectl rollout status $workload -n $namespace --timeout=6m } "$workload nao ficou pronto."
}

$seedSucceeded = kubectl get job conexao-solidaria-keycloak-seed `
    -n $namespace `
    --ignore-not-found `
    -o jsonpath='{.status.succeeded}'
if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel consultar o job de seed do Keycloak."
}
if ($Reseed -or $seedSucceeded -ne "1") {
    kubectl delete job conexao-solidaria-keycloak-seed -n $namespace --ignore-not-found=true --wait=true
    Invoke-Checked { kubectl apply -n $namespace -f deploy/kubernetes/local/keycloak-seed-job.yaml } "Falha ao aplicar o seed do Keycloak."
    Invoke-Checked { kubectl wait -n $namespace --for=condition=complete job/conexao-solidaria-keycloak-seed --timeout=8m } "O seed do Keycloak nao foi concluido."
}

$renderedChart = docker run --rm `
    -v "${root}:/src" `
    -w /src `
    alpine/helm:3.18.4 `
    template conexao-solidaria deploy/helm/conexao-solidaria `
    -f deploy/helm/conexao-solidaria/values.local.yaml `
    --set-string image.tag=$imageTag `
    --namespace $namespace
Apply-Generated $renderedChart "Falha ao aplicar o chart da aplicacao."

foreach ($component in $applicationWorkloads) {
    Invoke-Checked { kubectl rollout status "deployment/conexao-solidaria-$component" -n $namespace --timeout=6m } "$component nao ficou pronto."
}

Start-PortForwards
& "$PSScriptRoot\Initialize-Zabbix.ps1" `
    -BaseUrl "http://localhost:31092" `
    -GatewayUrl "http://conexao-solidaria-gateway:8080" `
    -WebUrl "http://conexao-solidaria-web" `
    -AdminPassword $environment.ZABBIX_ADMIN_PASSWORD

Write-Host ""
Write-Host "Conexao Solidaria esta pronta no Kubernetes local." -ForegroundColor Green
Write-Host "Imagens:    $imageTag"
Write-Host "Produto:    http://localhost:31080"
Write-Host "Gateway:    http://localhost:31081"
Write-Host "Keycloak:   http://localhost:31082"
Write-Host "RabbitMQ:   http://localhost:32672"
Write-Host "Grafana:    http://localhost:31090"
Write-Host "Prometheus: http://localhost:31091"
Write-Host "Zabbix:     http://localhost:31092 (Admin / senha em .env)"
Write-Host "API docs:   http://localhost:31101/scalar/v1 (Identity)"
Write-Host "            http://localhost:31102/scalar/v1 (Campaigns)"
Write-Host "            http://localhost:31103/scalar/v1 (Payments)"
Write-Host "            http://localhost:31104/scalar/v1 (Audit)"
Write-Host "            http://localhost:31105/scalar/v1 (Knowledge)"
Write-Host ""
Write-Host "Valide o fluxo com: .\scripts\Test-KubernetesLocal.ps1"
