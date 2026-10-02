<#
.SYNOPSIS
    Creates (or updates) the on-prem agent's own app registration, trusts its certificate, and gives it the
    Giim.Agent role on GIIM. Safe to run again.

.DESCRIPTION
    The agent signs in to GIIM as itself, with a certificate whose private key never leaves its server: no password
    or secret anywhere. Run this as an Entra administrator (Application Administrator plus the right to grant app
    roles: Privileged Role Administrator or Global Administrator), after New-GiimAppRegistration.ps1.

    First, on the agent's server (as an administrator), make the certificate and export its public half:

        $cert = New-SelfSignedCertificate -Subject 'CN=GIIM Agent' -CertStoreLocation Cert:\LocalMachine\My `
            -KeyExportPolicy NonExportable -KeySpec Signature -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2)
        Export-Certificate -Cert $cert -FilePath .\giim-agent.cer
        $cert.Thumbprint

    Then copy giim-agent.cer here and run this script. It prints the values for the agent's appsettings.json and the
    agentClientId for infra/params/<environment>.bicepparam. Renew the certificate before it expires: make a new one
    on the server, run this again with the new .cer (it replaces the old one), then put the new thumbprint in the
    agent's settings and restart the service.

    Needs: Install-Module Microsoft.Graph -Scope CurrentUser

.EXAMPLE
    ./infra/scripts/New-GiimAgentRegistration.ps1 -Environment prod -CertificatePath .\giim-agent.cer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('test', 'prod')]
    [string] $Environment,

    # The agent certificate's public part (.cer), exported on the agent's server.
    [Parameter(Mandatory)]
    [string] $CertificatePath,

    # GIIM's own app registration (as created by New-GiimAppRegistration.ps1).
    [string] $GiimDisplayName = $(if ($Environment -eq 'prod') { 'GIIM' } else { 'GIIM (test)' }),

    [string] $DisplayName = $(if ($Environment -eq 'prod') { 'GIIM Agent' } else { 'GIIM Agent (test)' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path $CertificatePath).Path)
if ($certificate.HasPrivateKey) { throw 'That file contains a private key. Export only the public certificate (.cer) from the agent server.' }

Connect-MgGraph -NoWelcome -Scopes @('Application.ReadWrite.All', 'AppRoleAssignment.ReadWrite.All')
$tenantId = (Get-MgContext).TenantId

# ---- GIIM, and its Giim.Agent role ----------------------------------------------------------------------------------

$giim = Get-MgApplication -Filter "displayName eq '$GiimDisplayName'" | Select-Object -First 1
if (-not $giim) { throw "GIIM's app registration '$GiimDisplayName' wasn't found. Run New-GiimAppRegistration.ps1 first." }
$role = $giim.AppRoles | Where-Object { $_.Value -eq 'Giim.Agent' }
if (-not $role) { throw "'$GiimDisplayName' has no Giim.Agent role yet. Run New-GiimAppRegistration.ps1 again (it adds it)." }
$giimSp = Get-MgServicePrincipal -Filter "appId eq '$($giim.AppId)'" | Select-Object -First 1

# ---- The agent's app registration, trusting its certificate --------------------------------------------------------

$agent = Get-MgApplication -Filter "displayName eq '$DisplayName'" | Select-Object -First 1
# Graph identifies a certificate credential by its thumbprint (CustomKeyIdentifier).
$trusted = $agent -and ($agent.KeyCredentials | Where-Object {
        $_.CustomKeyIdentifier -and (([BitConverter]::ToString($_.CustomKeyIdentifier) -replace '-', '') -eq $certificate.Thumbprint) })
$settings = @{
    DisplayName    = $DisplayName
    SignInAudience = 'AzureADMyOrg'
    Notes          = 'The GIIM on-prem agent. Signs in with a certificate on its server; holds only the Giim.Agent role on GIIM.'
}
if (-not $trusted) {
    # Graph doesn't return existing keys, so this sets the certificate list to just this one.
    if ($agent -and $agent.KeyCredentials.Count -gt 0) {
        Write-Warning 'This replaces the certificate the agent uses now: put the new thumbprint in its settings and restart it straight away.'
    }
    $settings.KeyCredentials = @(@{ Type = 'AsymmetricX509Cert'; Usage = 'Verify'; Key = $certificate.RawData
            DisplayName = "GIIM agent until $($certificate.NotAfter.ToString('yyyy-MM-dd'))" })
}
if ($agent) {
    Update-MgApplication -ApplicationId $agent.Id -BodyParameter $settings
    Write-Host "Updated '$DisplayName'."
}
else {
    $agent = New-MgApplication -BodyParameter $settings
    Write-Host "Created '$DisplayName'." -ForegroundColor Green
}
$agentSp = Get-MgServicePrincipal -Filter "appId eq '$($agent.AppId)'" | Select-Object -First 1
if (-not $agentSp) { $agentSp = New-MgServicePrincipal -AppId $agent.AppId }

# ---- Give the agent the Giim.Agent role on GIIM (and nothing else) -------------------------------------------------

$has = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $agentSp.Id -All |
    Where-Object { $_.ResourceId -eq $giimSp.Id -and $_.AppRoleId -eq $role.Id }
if (-not $has) {
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $agentSp.Id -PrincipalId $agentSp.Id -ResourceId $giimSp.Id -AppRoleId $role.Id | Out-Null
    Write-Host "Gave '$DisplayName' the Giim.Agent role on '$GiimDisplayName'." -ForegroundColor Green
}

Write-Host ''
Write-Host 'Done. In the agent''s appsettings.json (on its server):' -ForegroundColor Green
Write-Host "  Agent:Auth                  = Entra"
Write-Host "  Agent:TenantId              = $tenantId"
Write-Host "  Agent:ClientId              = $($agent.AppId)"
Write-Host "  Agent:GiimClientId          = $($giim.AppId)"
Write-Host "  Agent:CertificateThumbprint = $($certificate.Thumbprint)"
Write-Host ''
Write-Host "And in infra/params/$Environment.bicepparam: agentClientId = '$($agent.AppId)', then run ./infra/deploy.ps1 -Environment $Environment"
