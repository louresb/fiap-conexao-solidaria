param(
    [switch]$Build,
    [switch]$Tools,
    [switch]$Operations
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI nao encontrado. Instale ou inicie o Docker Desktop."
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop nao esta disponivel."
}

if (-not (Test-Path ".env")) {
    & "$PSScriptRoot\Initialize-LocalEnvironment.ps1"
}

$arguments = @("compose")
if ($Tools) {
    $arguments += @("--profile", "tools")
}
if ($Operations) {
    $arguments += @("--profile", "ops")
}

$arguments += @("up", "-d")
if ($Build) {
    $arguments += "--build"
}

& docker @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Nao foi possivel iniciar o ambiente local."
}

$deadline = [DateTime]::UtcNow.AddMinutes(4)
$ready = $false
while ([DateTime]::UtcNow -lt $deadline -and -not $ready) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:5100/health/ready" -TimeoutSec 5
        $ready = $response.StatusCode -eq 200
    }
    catch {
        Start-Sleep -Seconds 3
    }
}

if (-not $ready) {
    docker compose ps
    throw "O Gateway nao ficou pronto no tempo esperado. Consulte: docker compose logs --tail 100"
}

$environment = Get-Content ".env" -Raw | ConvertFrom-StringData
if ($Operations) {
    & "$PSScriptRoot\Initialize-Zabbix.ps1" `
        -BaseUrl "http://localhost:8085" `
        -GatewayUrl "http://gateway:8080" `
        -WebUrl "http://web:8080" `
        -AdminPassword $environment.ZABBIX_ADMIN_PASSWORD
}

Write-Host ""
Write-Host "Conexao Solidaria esta pronta." -ForegroundColor Green
Write-Host "Produto:    http://localhost:5000"
Write-Host "Keycloak:   http://localhost:8080"
Write-Host "RabbitMQ:   http://localhost:15672 (conexao / senha em .env)"
Write-Host "Grafana:    http://localhost:3000 (admin / $($environment.GRAFANA_ADMIN_PASSWORD))"
Write-Host "Prometheus: http://localhost:9090"
Write-Host "Tempo:      http://localhost:3200"
if ($Operations) {
    Write-Host "Zabbix:     http://localhost:8085 (Admin / senha em .env)"
}
Write-Host "Gestor:     gestor.esperanca@conexaosolidaria.local / $($environment.DEMO_MANAGER_PASSWORD)"
Write-Host "Doador:     doador.esperanca@conexaosolidaria.local / $($environment.DEMO_DONOR_PASSWORD)"
Write-Host "Admin:      admin.plataforma@conexaosolidaria.local / $($environment.DEMO_ADMIN_PASSWORD)"
Write-Host ""
Write-Host "Valide o fluxo completo com: .\scripts\Test-Local.ps1"
