param(
    [ValidatePattern("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    [string]$TenantId = "esperanca-solidaria",
    [ValidateRange(1, 10000)]
    [decimal]$DonationAmount = 73.25
)

$script = Join-Path $PSScriptRoot "Test-Local.ps1"
& $script -TenantId $TenantId -DonationAmount $DonationAmount -Runtime Kubernetes
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
