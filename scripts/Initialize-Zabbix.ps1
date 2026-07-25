param(
    [Parameter(Mandatory)]
    [ValidatePattern("^https?://")]
    [string]$BaseUrl,
    [Parameter(Mandatory)]
    [ValidatePattern("^https?://")]
    [string]$GatewayUrl,
    [Parameter(Mandatory)]
    [ValidatePattern("^https?://")]
    [string]$WebUrl,
    [Parameter(Mandatory)]
    [string]$AdminPassword
)

$ErrorActionPreference = "Stop"
$apiUrl = "$($BaseUrl.TrimEnd('/'))/api_jsonrpc.php"
$requestId = 0

function Invoke-ZabbixApi {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [hashtable]$Parameters,
        [string]$AuthToken
    )

    $script:requestId++
    $request = [ordered]@{
        jsonrpc = "2.0"
        method = $Method
        params = $Parameters
        id = $script:requestId
    }
    $headers = @{}
    if ($AuthToken) {
        $headers.Authorization = "Bearer $AuthToken"
    }

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri $apiUrl `
        -Headers $headers `
        -ContentType "application/json-rpc" `
        -Body ($request | ConvertTo-Json -Depth 12 -Compress) `
        -TimeoutSec 15

    if ($response.error) {
        throw "Zabbix API $Method falhou: $($response.error.data)"
    }

    return $response.result
}

$deadline = [DateTime]::UtcNow.AddMinutes(5)
$apiReady = $false
while ([DateTime]::UtcNow -lt $deadline -and -not $apiReady) {
    try {
        Invoke-ZabbixApi -Method "apiinfo.version" -Parameters @{} | Out-Null
        $apiReady = $true
    }
    catch {
        Start-Sleep -Seconds 3
    }
}
if (-not $apiReady) {
    throw "A API do Zabbix nao ficou disponivel em $apiUrl."
}

$session = $null
try {
    $session = Invoke-ZabbixApi -Method "user.login" -Parameters @{
        username = "Admin"
        password = $AdminPassword
        userData = $true
    }
}
catch {
    $session = Invoke-ZabbixApi -Method "user.login" -Parameters @{
        username = "Admin"
        password = "zabbix"
        userData = $true
    }

    Invoke-ZabbixApi -Method "user.update" -AuthToken $session.sessionid -Parameters @{
        userid = $session.userid
        current_passwd = "zabbix"
        passwd = $AdminPassword
        lang = "pt_BR"
    } | Out-Null

    $session = Invoke-ZabbixApi -Method "user.login" -Parameters @{
        username = "Admin"
        password = $AdminPassword
        userData = $true
    }
}

$authToken = $session.sessionid
try {
    $groupName = "Conexao Solidaria"
    $groups = @(Invoke-ZabbixApi -Method "hostgroup.get" -AuthToken $authToken -Parameters @{
        output = @("groupid", "name")
        filter = @{ name = @($groupName) }
    })
    if ($groups.Count -eq 0) {
        $groupResult = Invoke-ZabbixApi -Method "hostgroup.create" -AuthToken $authToken -Parameters @{
            name = $groupName
        }
        $groupId = $groupResult.groupids[0]
    }
    else {
        $groupId = $groups[0].groupid
    }

    $hostName = "Conexao Solidaria Platform"
    $hosts = @(Invoke-ZabbixApi -Method "host.get" -AuthToken $authToken -Parameters @{
        output = @("hostid", "host", "name")
        filter = @{ host = @($hostName) }
    })
    if ($hosts.Count -eq 0) {
        $hostResult = Invoke-ZabbixApi -Method "host.create" -AuthToken $authToken -Parameters @{
            host = $hostName
            name = "Conexao Solidaria"
            status = 0
            groups = @(@{ groupid = $groupId })
            tags = @(
                @{ tag = "system"; value = "conexao-solidaria" },
                @{ tag = "monitoring"; value = "http" }
            )
        }
        $hostId = $hostResult.hostids[0]
    }
    else {
        $hostId = $hosts[0].hostid
    }

    $scenarioName = "Jornada publica da plataforma"
    $steps = @(
        @{
            name = "Gateway readiness"
            url = "$($GatewayUrl.TrimEnd('/'))/health/ready"
            status_codes = "200"
            required = "Healthy"
            timeout = "10s"
            no = 1
        },
        @{
            name = "Campanhas ativas"
            url = "$($GatewayUrl.TrimEnd('/'))/api/public/campaigns?tenantId=esperanca-solidaria"
            status_codes = "200"
            required = "title"
            timeout = "10s"
            no = 2
        },
        @{
            name = "Produto web"
            url = $WebUrl.TrimEnd('/')
            status_codes = "200"
            required = "Conex"
            timeout = "10s"
            no = 3
        }
    )

    $scenarios = @(Invoke-ZabbixApi -Method "httptest.get" -AuthToken $authToken -Parameters @{
        output = @("httptestid", "name")
        hostids = @($hostId)
        filter = @{ name = @($scenarioName) }
    })
    if ($scenarios.Count -eq 0) {
        Invoke-ZabbixApi -Method "httptest.create" -AuthToken $authToken -Parameters @{
            name = $scenarioName
            hostid = $hostId
            delay = "30s"
            retries = 2
            steps = $steps
            tags = @(@{ tag = "journey"; value = "public" })
        } | Out-Null
    }
    else {
        Invoke-ZabbixApi -Method "httptest.update" -AuthToken $authToken -Parameters @{
            httptestid = $scenarios[0].httptestid
            name = $scenarioName
            delay = "30s"
            retries = 2
            steps = $steps
            tags = @(@{ tag = "journey"; value = "public" })
        } | Out-Null
    }
}
finally {
    Invoke-ZabbixApi -Method "user.logout" -AuthToken $authToken -Parameters @{} | Out-Null
}

Write-Host "Zabbix configurado com a jornada publica da plataforma." -ForegroundColor Green
