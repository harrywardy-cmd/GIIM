<#
.SYNOPSIS
    Creates or updates one GIIM environment in Azure (infrastructure only; code is deployed by GitHub Actions).

.DESCRIPTION
    Shows exactly what will change and asks before changing anything. Run by someone with Owner (or Contributor +
    User Access Administrator) on the subscription or resource group, signed in with `az login`.
    See infra/README.md for the full runbook.

.EXAMPLE
    ./infra/deploy.ps1 -Environment test -WhatIf          # preview only
    ./infra/deploy.ps1 -Environment test                  # preview, confirm, deploy
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('test', 'prod')]
    [string] $Environment,

    [string] $ResourceGroup = "rg-giim-$Environment",

    [string] $Location = 'australiaeast',

    # Preview the changes without making them.
    [switch] $WhatIf,

    # Ask for the ServiceDesk Plus client secret and refresh token, and set the webhook secret (generated if left blank).
    [switch] $SetServiceDeskSecrets
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Az {
    $output = az @args
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($args -join ' ')" }
    return $output
}

$paramsFile = Join-Path $PSScriptRoot "params/$Environment.bicepparam"
$deploymentName = 'giim-main'

$account = az account show --query '{name:name, id:id, user:user.name}' -o json 2>$null | ConvertFrom-Json
if (-not $account) { throw 'Not signed in to Azure. Run: az login' }
Write-Host "Subscription : $($account.name) ($($account.id))" -ForegroundColor Cyan
Write-Host "Signed in as : $($account.user)" -ForegroundColor Cyan
Write-Host "Environment  : $Environment -> resource group $ResourceGroup ($Location)" -ForegroundColor Cyan

if ((Invoke-Az group exists --name $ResourceGroup) -ne 'true') {
    if ($WhatIf) {
        Write-Host "Resource group $ResourceGroup doesn't exist yet; it will be created on a real run." -ForegroundColor Yellow
        return
    }
    Write-Host "Creating resource group $ResourceGroup..."
    Invoke-Az group create --name $ResourceGroup --location $Location --tags application=GIIM environment=$Environment | Out-Null
}

function Read-Secret([string] $prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

$newWebhookSecret = $null
try {
    if ($SetServiceDeskSecrets) {
        # Passed to the deployment through environment variables (read by the .bicepparam file), never a command line or file.
        $env:GIIM_SDP_CLIENT_SECRET = Read-Secret 'ServiceDesk Plus client secret (input hidden; blank = keep)'
        $env:GIIM_SDP_REFRESH_TOKEN = Read-Secret 'ServiceDesk Plus refresh token (input hidden; blank = keep)'
        $webhook = Read-Secret 'Webhook secret (input hidden; blank = generate a new one)'
        if (-not $webhook) {
            $bytes = [byte[]]::new(32)
            [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
            $webhook = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            $newWebhookSecret = $webhook
        }
        $env:GIIM_SDP_WEBHOOK_SECRET = $webhook
    }

    # Run directly (not captured) so the preview of changes shows before the confirmation prompt.
    if ($WhatIf) {
        az deployment group what-if --resource-group $ResourceGroup --name $deploymentName --parameters $paramsFile
        if ($LASTEXITCODE -ne 0) { throw 'Preview failed (see the message above).' }
        return
    }

    az deployment group create --resource-group $ResourceGroup --name $deploymentName --parameters $paramsFile `
        --confirm-with-what-if --output none
    if ($LASTEXITCODE -ne 0) { throw 'Deployment failed (see the message above).' }
}
finally {
    foreach ($name in 'GIIM_SDP_CLIENT_SECRET', 'GIIM_SDP_REFRESH_TOKEN', 'GIIM_SDP_WEBHOOK_SECRET') {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}

$state = az deployment group show --resource-group $ResourceGroup --name $deploymentName --query properties.provisioningState -o tsv
if ($state -ne 'Succeeded') {
    Write-Host 'Deployment was cancelled or did not succeed; nothing more to do.' -ForegroundColor Yellow
    return
}

$o = Invoke-Az deployment group show --resource-group $ResourceGroup --name $deploymentName --query properties.outputs -o json |
    ConvertFrom-Json

Write-Host ''
Write-Host "GIIM $Environment is deployed: $($o.publicUrl.value)" -ForegroundColor Green
Write-Host ''
Write-Host 'GitHub environment variables (Settings > Environments >' $Environment '> Environment variables):' -ForegroundColor Cyan
$variables = [ordered]@{
    AZURE_CLIENT_ID       = $o.deployClientId.value
    AZURE_TENANT_ID       = $o.tenantId.value
    AZURE_SUBSCRIPTION_ID = $o.subscriptionId.value
    RESOURCE_GROUP        = $o.resourceGroupName.value
    API_APP_NAME          = $o.apiAppName.value
    WORKERS_APP_NAME      = $o.workersAppName.value
    SQL_SERVER_NAME       = $o.sqlServerName.value
    SQL_SERVER_FQDN       = $o.sqlServerFqdn.value
    DATABASE_NAME         = $o.databaseName.value
    PUBLIC_URL            = $o.publicUrl.value
}
$variables.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-22} {1}" -f $_.Key, $_.Value) }
Write-Host ''
Write-Host 'For the Entra administrator (docs/entra-setup.md, or infra/scripts/New-GiimAppRegistration.ps1):' -ForegroundColor Cyan
Write-Host "  Redirect URI (web)       : $($o.signInRedirectUri.value)"
Write-Host "  Front-channel logout URL : $($o.signOutRedirectUri.value)"
Write-Host "  My Apps home page URL    : $($o.myAppsHomePageUrl.value)"
Write-Host "  Trusted managed identity : object ID $($o.apiIdentityPrincipalId.value) (client ID $($o.apiIdentityClientId.value))"
Write-Host ''
Write-Host "GIIM's outbound IP address (for allow-lists): $($o.outboundIpAddress.value)" -ForegroundColor Cyan
Write-Host ''
Write-Host 'For the Exchange administrator (sending emails, see docs/email-notifications.md):' -ForegroundColor Cyan
Write-Host "  Workers identity client ID : $($o.workersIdentityClientId.value)"
Write-Host "  Workers identity object ID : $($o.workersIdentityPrincipalId.value)"
Write-Host ''
Write-Host 'For the ServiceDesk Plus administrator (docs/servicedesk-setup.md):' -ForegroundColor Cyan
Write-Host "  Webhook URL    : $($o.serviceDeskWebhookUrl.value)"
Write-Host '  Header         : X-GIIM-Webhook-Secret'
if ($newWebhookSecret) {
    Write-Host "  Webhook secret : $newWebhookSecret" -ForegroundColor Yellow
    Write-Host '  (Shown once. Give it to the ServiceDesk Plus administrator through a password manager, not email or Teams.)' -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Next (first deployment only): infra/scripts/Grant-SqlAccess.ps1 and infra/scripts/Grant-GraphAccess.ps1' -ForegroundColor Yellow
