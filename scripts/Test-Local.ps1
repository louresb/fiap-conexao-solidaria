param(
    [ValidatePattern("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    [string]$TenantId = "esperanca-solidaria",
    [ValidateRange(1, 10000)]
    [decimal]$DonationAmount = 73.25,
    [ValidateSet("Compose", "Kubernetes")]
    [string]$Runtime = "Compose"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$gateway = if ($Runtime -eq "Kubernetes") { "http://localhost:31081" } else { "http://localhost:5100" }
$keycloak = if ($Runtime -eq "Kubernetes") { "http://localhost:31082" } else { "http://localhost:8080" }

function Write-Step([string]$Message) {
    Write-Host "[check] $Message" -ForegroundColor Cyan
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "Falha: $Message"
    }
}

function Get-AccessToken([string]$Username, [string]$Password) {
    $request = @{
        Method = "Post"
        Uri = "$keycloak/realms/conexao-solidaria/protocol/openid-connect/token"
        Body = @{
            client_id = "conexao-cli"
            grant_type = "password"
            username = $Username
            password = $Password
        }
    }
    $response = Invoke-RestMethod @request

    return $response.access_token
}

function Wait-Until([scriptblock]$Probe, [string]$FailureMessage, [int]$Attempts = 30) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $result = & $Probe
        if ($null -ne $result) {
            return $result
        }

        Start-Sleep -Milliseconds 500
    }

    throw $FailureMessage
}

if (-not (Test-Path ".env")) {
    throw "Arquivo .env ausente. Execute .\scripts\Start-Local.ps1 -Build primeiro."
}

$environment = Get-Content ".env" -Raw | ConvertFrom-StringData
$tenantHeader = @{ "X-Tenant-Id" = $TenantId }

Write-Step "Readiness agregado"
$readiness = Invoke-RestMethod "$gateway/health/ready"
Assert-True ($readiness.status -eq "Healthy") "Gateway ou dependencias indisponiveis."

Write-Step "Cache Redis MISS/HIT"
$cacheKey = "active-campaigns:$TenantId"
if ($Runtime -eq "Kubernetes") {
    kubectl exec -n conexao-solidaria deployment/conexao-solidaria-redis -- `
        redis-cli --no-auth-warning -a $environment.REDIS_PASSWORD DEL $cacheKey *> $null
}
else {
    docker compose exec -T redis redis-cli --no-auth-warning -a $environment.REDIS_PASSWORD DEL $cacheKey *> $null
}
Assert-True ($LASTEXITCODE -eq 0) "Nao foi possivel limpar a chave de teste no Redis."

$firstTimer = [Diagnostics.Stopwatch]::StartNew()
$firstResponse = Invoke-WebRequest -UseBasicParsing "$gateway/api/public/campaigns?tenantId=$TenantId"
$firstTimer.Stop()
$secondTimer = [Diagnostics.Stopwatch]::StartNew()
$secondResponse = Invoke-WebRequest -UseBasicParsing "$gateway/api/public/campaigns?tenantId=$TenantId"
$secondTimer.Stop()
Assert-True ($firstResponse.Headers["X-Cache"] -eq "MISS") "A primeira leitura deveria ser MISS."
Assert-True ($secondResponse.Headers["X-Cache"] -eq "HIT") "A segunda leitura deveria ser HIT."
$campaigns = $secondResponse.Content | ConvertFrom-Json
Assert-True ($campaigns.Count -gt 0) "Nenhuma campanha ativa foi retornada."

Write-Step "Busca fuzzy no OpenSearch"
$search = Invoke-RestMethod "$gateway/api/public/campaigns/search?q=mesa%20xeia&tenantId=$TenantId&limit=5"
Assert-True ($search.Count -gt 0) "A busca fuzzy nao encontrou a campanha esperada."

Write-Step "JWT, RBAC e isolamento multi-tenant"
$donorToken = Get-AccessToken "doador.esperanca@conexaosolidaria.local" $environment.DEMO_DONOR_PASSWORD
$donorHeaders = @{
    Authorization = "Bearer $donorToken"
    "X-Correlation-Id" = "smoke-$([Guid]::NewGuid().ToString('N'))"
}

$forbidden = $false
try {
    Invoke-WebRequest -UseBasicParsing "$gateway/api/management/campaigns" -Headers $donorHeaders | Out-Null
}
catch {
    $forbidden = $_.Exception.Response.StatusCode.value__ -eq 403
}
Assert-True $forbidden "Um doador nao pode acessar a gestao de campanhas."

$otherTenantCampaign = (Invoke-RestMethod "$gateway/api/public/campaigns?tenantId=mare-limpa")[0]
$crossTenantBody = @{
    campaignId = $otherTenantCampaign.id
    amount = 10
    paymentMethod = "pix"
} | ConvertTo-Json
$isolated = $false
try {
    Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$gateway/api/donations" -Headers $donorHeaders -ContentType "application/json" -Body $crossTenantBody | Out-Null
}
catch {
    $isolated = $_.Exception.Response.StatusCode.value__ -eq 404
}
Assert-True $isolated "O tenant autenticado nao deve acessar campanha de outro tenant."

Write-Step "Doacao, pagamento e processamento assincrono"
$campaign = $campaigns[0]
$beforeTotal = [decimal]$campaign.totalRaised
$donationBody = @{
    campaignId = $campaign.id
    amount = $DonationAmount
    paymentMethod = "pix"
} | ConvertTo-Json
$donation = Invoke-RestMethod -Method Post -Uri "$gateway/api/donations" -Headers $donorHeaders -ContentType "application/json" -Body $donationBody

$payment = Wait-Until -FailureMessage "O pagamento nao foi criado pelo consumidor." -Probe {
    try {
        Invoke-RestMethod "$gateway/api/payments/donations/$($donation.id)" -Headers $donorHeaders
    }
    catch {
        $null
    }
}

$confirmation = Invoke-RestMethod `
    -Method Post `
    -Uri "$gateway/api/payments/$($payment.id)/simulate-confirmation" `
    -Headers $donorHeaders `
    -ContentType "application/json" `
    -Body "{}"
Assert-True ($confirmation.payment.status -eq "Confirmed") "O pagamento deveria estar confirmado."

$updatedCampaign = Wait-Until -FailureMessage "O total da campanha nao foi atualizado pelo worker." -Probe {
    $current = Invoke-RestMethod "$gateway/api/public/campaigns?tenantId=$TenantId"
    $candidate = $current | Where-Object id -eq $campaign.id
    if ($null -ne $candidate -and [decimal]$candidate.totalRaised -ge ($beforeTotal + $DonationAmount)) {
        $candidate
    }
}

Write-Step "Auditoria append-only e correlacionada"
$managerToken = Get-AccessToken "gestor.esperanca@conexaosolidaria.local" $environment.DEMO_MANAGER_PASSWORD
$managerHeaders = @{ Authorization = "Bearer $managerToken" }
$auditEvents = Wait-Until -FailureMessage "A trilha de auditoria correlacionada nao foi materializada." -Probe {
    $events = Invoke-RestMethod "$gateway/api/audit/$($donorHeaders['X-Correlation-Id'])" -Headers $managerHeaders
    if ($events.Count -ge 3) { $events }
}
Assert-True (($auditEvents | Where-Object tenantId -ne $TenantId).Count -eq 0) "A auditoria retornou dados de outro tenant."

Write-Step "Resposta baseada em fontes"
$knowledge = Invoke-RestMethod `
    -Method Post `
    -Uri "$gateway/api/knowledge/ask" `
    -Headers $tenantHeader `
    -ContentType "application/json" `
    -Body (@{ question = "Como funciona a prestacao de contas das campanhas?" } | ConvertTo-Json)
Assert-True $knowledge.answered "A base de conhecimento nao respondeu."
Assert-True ($knowledge.sources.Count -gt 0) "A resposta deve indicar pelo menos uma fonte."

if ($Runtime -eq "Kubernetes") {
    Write-Step "Pods e coleta de metricas"
    $pods = kubectl get pods -n conexao-solidaria -o json | ConvertFrom-Json
    $unreadyPods = @($pods.items | Where-Object {
        $_.status.phase -ne "Succeeded" -and
        @($_.status.containerStatuses | Where-Object { -not $_.ready }).Count -gt 0
    })
    Assert-True ($unreadyPods.Count -eq 0) "Existem pods Kubernetes sem readiness."

    $prometheus = Invoke-RestMethod "http://localhost:31091/api/v1/query?query=up"
    $downTargets = @($prometheus.data.result | Where-Object { $_.value[1] -ne "1" })
    Assert-True ($downTargets.Count -eq 0) "O Prometheus encontrou targets indisponiveis."

    $grafana = Invoke-RestMethod "http://localhost:31090/api/health"
    Assert-True ($grafana.database -eq "ok") "O Grafana nao esta saudavel."
}

Write-Host ""
Write-Host "Smoke test concluido com sucesso." -ForegroundColor Green
[pscustomobject]@{
    Tenant = $TenantId
    CacheFirst = $firstResponse.Headers["X-Cache"]
    CacheSecond = $secondResponse.Headers["X-Cache"]
    FirstRequestMs = $firstTimer.ElapsedMilliseconds
    CachedRequestMs = $secondTimer.ElapsedMilliseconds
    FuzzyResult = $search[0].title
    DonationId = $donation.id
    PaymentId = $payment.id
    CampaignTotalBefore = $beforeTotal
    CampaignTotalAfter = $updatedCampaign.totalRaised
    CorrelatedAuditEvents = $auditEvents.Count
    KnowledgeSources = $knowledge.sources.Count
    Runtime = $Runtime
} | Format-List
