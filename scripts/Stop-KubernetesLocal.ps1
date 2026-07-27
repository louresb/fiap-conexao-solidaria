param(
    [switch]$PurgeData
)

$ErrorActionPreference = "Stop"
$namespace = "conexao-solidaria"
$root = Split-Path -Parent $PSScriptRoot
$portForwardState = Join-Path $root ".local\kubernetes-port-forwards.json"

if (Test-Path $portForwardState) {
    $processIds = Get-Content $portForwardState -Raw | ConvertFrom-Json
    foreach ($processId in @($processIds)) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $portForwardState -Force -ErrorAction SilentlyContinue
}

if ($PurgeData) {
    kubectl delete namespace $namespace --ignore-not-found=true --wait=true
    if ($LASTEXITCODE -ne 0) {
        throw "Nao foi possivel remover o ambiente Kubernetes local."
    }
    Write-Host "Ambiente Kubernetes local removido com os dados persistentes."
    exit 0
}

$deployments = kubectl get deployments -n $namespace -o name 2>$null
$statefulSets = kubectl get statefulsets -n $namespace -o name 2>$null
if ($deployments) {
    $deployments | ForEach-Object { kubectl scale $_ -n $namespace --replicas=0 }
}
if ($statefulSets) {
    $statefulSets | ForEach-Object { kubectl scale $_ -n $namespace --replicas=0 }
}

Write-Host "Workloads locais pausados. PVCs e configuracoes foram preservados."
