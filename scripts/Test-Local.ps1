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
$tempo = if ($Runtime -eq "Kubernetes") { "http://localhost:31093" } else { "http://localhost:3200" }

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

function Invoke-ZabbixApi([string]$Method, [hashtable]$Parameters, [string]$AuthToken) {
    $request = [ordered]@{
        jsonrpc = "2.0"
        method = $Method
        params = $Parameters
        id = 1
    }
    $headers = @{}
    if ($AuthToken) {
        $headers.Authorization = "Bearer $AuthToken"
    }

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:31092/api_jsonrpc.php" `
        -Headers $headers `
        -ContentType "application/json-rpc" `
        -Body ($request | ConvertTo-Json -Depth 8 -Compress)
    if ($response.error) {
        throw "Zabbix API $Method falhou: $($response.error.data)"
    }

    return $response.result
}

function Wait-Until(
    [scriptblock]$Probe,
    [string]$FailureMessage,
    [int]$Attempts = 30,
    [int]$DelayMilliseconds = 500
) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $result = & $Probe
        if ($null -ne $result) {
            return $result
        }

        Start-Sleep -Milliseconds $DelayMilliseconds
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

Write-Step "Publicacao de campanha via Outbox"
$campaignCorrelationId = "campaign-smoke-$([Guid]::NewGuid().ToString('N'))"
$campaignHeaders = @{
    Authorization = "Bearer $managerToken"
    "X-Correlation-Id" = $campaignCorrelationId
}
$campaignBody = @{
    title = "Campanha de validacao $([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    description = "Campanha criada automaticamente para validar publicacao e auditoria via Outbox."
    startDate = (Get-Date).ToString("yyyy-MM-dd")
    endDate = (Get-Date).AddDays(30).ToString("yyyy-MM-dd")
    goalAmount = 1000
    status = 4
} | ConvertTo-Json
$createdCampaign = Invoke-RestMethod `
    -Method Post `
    -Uri "$gateway/api/management/campaigns" `
    -Headers $campaignHeaders `
    -ContentType "application/json" `
    -Body $campaignBody
foreach ($nextStatus in @(5, 6, 1)) {
    $transitionBody = @{
        title = $createdCampaign.title
        description = $createdCampaign.description
        startDate = $createdCampaign.startDate
        endDate = $createdCampaign.endDate
        goalAmount = $createdCampaign.goalAmount
        status = $nextStatus
    } | ConvertTo-Json
    $createdCampaign = Invoke-RestMethod `
        -Method Put `
        -Uri "$gateway/api/management/campaigns/$($createdCampaign.id)" `
        -Headers $campaignHeaders `
        -ContentType "application/json" `
        -Body $transitionBody
}
$campaignEvents = Wait-Until `
    -Attempts 60 `
    -DelayMilliseconds 1000 `
    -FailureMessage "Os eventos de criacao e publicacao da campanha nao foram auditados. CorrelationId: $campaignCorrelationId" `
    -Probe {
    $events = @(Invoke-RestMethod "$gateway/api/audit/$campaignCorrelationId" -Headers $managerHeaders | ForEach-Object { $_ })
    $eventTypes = @($events | ForEach-Object eventType)
    if ($eventTypes -contains "CampaignCreated" -and $eventTypes -contains "CampaignPublished") {
        $events
    }
}

Write-Step "Read model publico e anonimizado"
$transparency = Wait-Until -FailureMessage "A doacao nao foi materializada na transparencia publica." -Probe {
    $snapshot = Invoke-RestMethod "$gateway/api/public/transparency?tenantId=$TenantId&limit=30"
    if (@($snapshot.recentDonations | Where-Object donationId -eq $donation.id).Count -eq 1) {
        $snapshot
    }
}
Assert-True (
    $null -eq ($transparency.recentDonations | Where-Object { $_.PSObject.Properties.Name -contains "donorEmail" })
) "A projecao publica nao deve expor dados pessoais."

Write-Step "Resposta baseada em fontes"
$knowledge = Invoke-RestMethod `
    -Method Post `
    -Uri "$gateway/api/knowledge/ask" `
    -Headers $tenantHeader `
    -ContentType "application/json" `
    -Body (@{ question = "Como funciona a prestacao de contas das campanhas?" } | ConvertTo-Json)
Assert-True $knowledge.answered "A base de conhecimento nao respondeu."
Assert-True ($knowledge.sources.Count -gt 0) "A resposta deve indicar pelo menos uma fonte."

Write-Step "Tracing distribuido no Tempo"
$tempoReady = Wait-Until `
    -Attempts 30 `
    -DelayMilliseconds 1000 `
    -FailureMessage "O Tempo nao ficou pronto dentro do prazo esperado." `
    -Probe {
        try {
            $response = Invoke-WebRequest -UseBasicParsing "$tempo/ready"
            if ($response.StatusCode -eq 200) { return $response }
        }
        catch {
            return $null
        }
    }
Assert-True ($tempoReady.StatusCode -eq 200) "O Tempo nao esta saudavel."
$expectedTraceServices = @("gateway", "donations-api", "campaigns-api", "campaigns-worker", "payments-api", "audit-api", "knowledge-api")
$tracedServices = Wait-Until `
    -Attempts 120 `
    -DelayMilliseconds 1000 `
    -FailureMessage "Os traces da jornada nao foram materializados no Tempo." `
    -Probe {
        $tagResponse = Invoke-RestMethod "$tempo/api/search/tag/service.name/values"
        $values = @($tagResponse.tagValues | ForEach-Object {
            if ($_ -is [string]) { $_ } elseif ($_.value) { $_.value }
        })
        $missing = @($expectedTraceServices | Where-Object { $_ -notin $values })
        if ($missing.Count -eq 0) { return $values }
        return $null
    }
$journeyTraceServices = @("gateway", "donations-api", "campaigns-api", "campaigns-worker", "payments-api", "audit-api")
$traceQuery = [uri]::EscapeDataString("{ span.app.correlation_id = `"$($donorHeaders['X-Correlation-Id'])`" }")
$journeyTrace = Wait-Until `
    -Attempts 120 `
    -DelayMilliseconds 1000 `
    -FailureMessage "O trace distribuido da doacao nao percorreu todos os servicos esperados." `
    -Probe {
        $traceSearch = Invoke-RestMethod "$tempo/api/search?q=$traceQuery&limit=20"
        $candidate = @($traceSearch.traces | Where-Object {
            $traceServices = @($_.serviceStats.PSObject.Properties.Name)
            $_.rootTraceName -like "POST /api/donations/*" -and
            @($journeyTraceServices | Where-Object { $_ -notin $traceServices }).Count -eq 0
        } | Select-Object -First 1)
        if ($candidate.Count -eq 1) { return $candidate[0] }
        return $null
    }

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

    $applicationJobs = @(
        "gateway", "web", "identity-api", "campaigns-api", "campaigns-worker",
        "donations-api", "payments-api", "audit-api", "knowledge-api"
    )
    $targets = (Invoke-RestMethod "http://localhost:31091/api/v1/targets").data.activeTargets
    $applicationTargets = @($targets | Where-Object { $_.labels.job -in $applicationJobs })
    Assert-True ($applicationTargets.Count -eq $applicationJobs.Count) `
        "O Prometheus nao possui um target para cada runtime da aplicacao."
    Assert-True (@($applicationTargets | Where-Object health -ne "up").Count -eq 0) `
        "Existe runtime da aplicacao sem coleta de metricas."

    $jobMatcher = $applicationJobs -join "|"
    $cpuQuery = [uri]::EscapeDataString(
        "sum by (job) (rate(process_cpu_seconds_total{job=~`"$jobMatcher`"}[2m]))")
    $memoryQuery = [uri]::EscapeDataString(
        "sum by (job) (process_working_set_bytes{job=~`"$jobMatcher`"})")
    $cpuSeries = (Invoke-RestMethod "http://localhost:31091/api/v1/query?query=$cpuQuery").data.result
    $memorySeries = (Invoke-RestMethod "http://localhost:31091/api/v1/query?query=$memoryQuery").data.result
    Assert-True (@($cpuSeries).Count -eq $applicationJobs.Count) `
        "As series de CPU nao cobrem todos os runtimes."
    Assert-True (@($memorySeries).Count -eq $applicationJobs.Count) `
        "As series de memoria nao cobrem todos os runtimes."

    $grafana = Invoke-RestMethod "http://localhost:31090/api/health"
    Assert-True ($grafana.database -eq "ok") "O Grafana nao esta saudavel."
    $grafanaCredentials = [Convert]::ToBase64String(
        [Text.Encoding]::ASCII.GetBytes("admin:$($environment.GRAFANA_ADMIN_PASSWORD)"))
    $grafanaHeaders = @{ Authorization = "Basic $grafanaCredentials" }
    $dashboard = (Invoke-RestMethod `
        -Headers $grafanaHeaders `
        -Uri "http://localhost:31090/api/dashboards/uid/conexao-solidaria-operacao").dashboard
    $dashboardTitles = @($dashboard.panels | ForEach-Object title)
    Assert-True (@($dashboardTitles | Where-Object { $_ -like "CPU por servi*" }).Count -eq 1) `
        "O dashboard nao possui o painel de CPU."
    Assert-True (@($dashboardTitles | Where-Object { $_ -like "Mem*ria por servi*" }).Count -eq 1) `
        "O dashboard nao possui o painel de memoria."
    $tempoDatasource = Invoke-WebRequest `
        -UseBasicParsing `
        -Headers $grafanaHeaders `
        -Uri "http://localhost:31090/api/datasources/proxy/uid/Tempo/ready"
    Assert-True ($tempoDatasource.StatusCode -eq 200) "O datasource Tempo nao esta acessivel pelo Grafana."

    $zabbixPage = Invoke-WebRequest -UseBasicParsing "http://localhost:31092/"
    Assert-True ($zabbixPage.StatusCode -eq 200) "A interface do Zabbix nao esta saudavel."
    $zabbixToken = Invoke-ZabbixApi -Method "user.login" -Parameters @{
        username = "Admin"
        password = $environment.ZABBIX_ADMIN_PASSWORD
    }
    try {
        $zabbixScenarios = @(Invoke-ZabbixApi -Method "httptest.get" -AuthToken $zabbixToken -Parameters @{
            output = @("httptestid", "name", "hostid")
            filter = @{ name = @("Jornada publica da plataforma") }
        })
        Assert-True ($zabbixScenarios.Count -eq 1) "O web scenario da plataforma nao esta configurado no Zabbix."

        $zabbixResponseCodes = Wait-Until `
            -Attempts 120 `
            -DelayMilliseconds 1000 `
            -FailureMessage "O Zabbix nao coletou a jornada publica com sucesso." `
            -Probe {
                $items = @(Invoke-ZabbixApi -Method "item.get" -AuthToken $zabbixToken -Parameters @{
                    output = @("name", "key_", "lastvalue", "lastclock")
                    hostids = @($zabbixScenarios[0].hostid)
                    webitems = $true
                })
                $responseCodes = @($items | Where-Object { $_.key_ -like "web.test.rspcode*" })
                $failures = @($responseCodes | Where-Object {
                    $_.lastvalue -ne "200" -or [int64]$_.lastclock -eq 0
                })
                if ($responseCodes.Count -eq 3 -and $failures.Count -eq 0) {
                    return $responseCodes
                }

                return $null
            }
        Assert-True ($zabbixResponseCodes.Count -eq 3) "O Zabbix nao confirmou os tres passos da jornada."
    }
    finally {
        Invoke-ZabbixApi -Method "user.logout" -Parameters @{} -AuthToken $zabbixToken | Out-Null
    }
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
    PublicDonationHistory = $transparency.recentDonations.Count
    CorrelatedAuditEvents = $auditEvents.Count
    CampaignLifecycleEvents = $campaignEvents.Count
    KnowledgeSources = $knowledge.sources.Count
    TracedServices = @($tracedServices).Count
    DistributedTraceId = $journeyTrace.traceID
    Runtime = $Runtime
} | Format-List
