<#
.SYNOPSIS
    Creates GIIM's database users for its managed identities (first deployment, or after recreating identities).

.DESCRIPTION
    Run by a member of the SQL admin group, signed in with `az login`. Opens the database firewall to this PC's
    address for the duration of the script and always closes it again. Needs the modern sqlcmd:
    winget install sqlcmd
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('test', 'prod')]
    [string] $Environment,

    [string] $ResourceGroup = "rg-giim-$Environment"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw 'sqlcmd not found. Install it with: winget install sqlcmd' }

$o = az deployment group show --resource-group $ResourceGroup --name giim-main --query properties.outputs -o json | ConvertFrom-Json
if (-not $o) { throw "No GIIM deployment found in $ResourceGroup. Run infra/deploy.ps1 first." }

$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
$rule = "admin-temp-$($env:USERNAME)".ToLowerInvariant()
Write-Host "Opening the database firewall to $myIp for this script..."
az sql server firewall-rule create --resource-group $ResourceGroup --server $o.sqlServerName.value --name $rule `
    --start-ip-address $myIp --end-ip-address $myIp --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not add the firewall rule.' }

try {
    Start-Sleep -Seconds 15   # firewall changes take a few seconds to apply
    sqlcmd -S "tcp:$($o.sqlServerFqdn.value),1433" -d $o.databaseName.value --authentication-method ActiveDirectoryDefault -b `
        -i (Join-Path $PSScriptRoot '../sql/grant-access.sql') `
        -v "ApiIdentity=$($o.apiIdentityName.value)" "WorkersIdentity=$($o.workersIdentityName.value)" "DeployIdentity=$($o.deployIdentityName.value)"
    if ($LASTEXITCODE -ne 0) { throw 'Granting database access failed (see the message above).' }
    Write-Host 'Database access granted.' -ForegroundColor Green
}
finally {
    Write-Host 'Closing the database firewall again...'
    az sql server firewall-rule delete --resource-group $ResourceGroup --server $o.sqlServerName.value --name $rule --only-show-errors
}
