<#
.SYNOPSIS
    Creates (or updates) the GIIM app registration in Microsoft Entra ID, so staff sign in from the GIIM tile in
    My Apps. Safe to run again: it only adds what is missing.

.DESCRIPTION
    Run by an Entra administrator (Application Administrator, or Cloud Application Administrator plus the right
    to grant admin consent), after infra/deploy.ps1 has created the environment. It sets up:
      - sign-in and sign-out addresses, and the My Apps home page (which starts sign-in straight away)
      - the four GIIM app roles: Administrator, Technician, Manager, Viewer
      - "assignment required", so only people you assign (directly or through a group) can sign in or see the tile
      - trust in GIIM's managed identity as the app's credential, so there is no client secret to store or renew
      - the email claim, and admin consent for the openid, profile and email permissions sign-in needs
      - optionally, which groups get which role
    Then put the client ID it prints into infra/params/<environment>.bicepparam (entraClientId) and deploy again.

    Needs: Install-Module Microsoft.Graph -Scope CurrentUser   and   az login (to read the deployment).

.EXAMPLE
    ./infra/scripts/New-GiimAppRegistration.ps1 -Environment prod `
        -AdministratorsGroup 'GIIM-Administrators' -TechniciansGroup 'GIIM-Technicians' `
        -ManagersGroup 'GIIM-Managers' -ViewersGroup 'GIIM-Viewers'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('test', 'prod')]
    [string] $Environment,

    [string] $ResourceGroup = "rg-giim-$Environment",

    # Name shown on the My Apps tile.
    [string] $DisplayName = $(if ($Environment -eq 'prod') { 'GIIM' } else { 'GIIM (test)' }),

    # Optional: security groups (display name or object ID) to give each role. Groups synced from AD work.
    [string] $AdministratorsGroup,
    [string] $TechniciansGroup,
    [string] $ManagersGroup,
    [string] $ViewersGroup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$o = az deployment group show --resource-group $ResourceGroup --name giim-main --query properties.outputs -o json | ConvertFrom-Json
if (-not $o) { throw "No GIIM deployment found in $ResourceGroup. Run infra/deploy.ps1 first." }
$tenantId = $o.tenantId.value

Connect-MgGraph -TenantId $tenantId -NoWelcome -Scopes @(
    'Application.ReadWrite.All', 'AppRoleAssignment.ReadWrite.All', 'DelegatedPermissionGrant.ReadWrite.All', 'Group.Read.All')

# ---- The app registration ------------------------------------------------------------------------------------------

$roles = [ordered]@{
    Administrator = 'Everything, including locations, categories, register import and directory sync'
    Technician    = 'Add, assign, return, repair, retire and label assets; stock; process device requests'
    Manager       = 'Read everything; raise device requests and approve those they are the approver for'
    Viewer        = 'Read everything'
}

$app = Get-MgApplication -Filter "displayName eq '$DisplayName'" | Select-Object -First 1
$existingRoles = if ($app) { @($app.AppRoles) } else { @() }
$appRoles = @($existingRoles)
foreach ($role in $roles.GetEnumerator()) {
    if ($existingRoles | Where-Object { $_.Value -eq $role.Key }) { continue }
    $appRoles += @{
        Id                 = [guid]::NewGuid()
        Value              = $role.Key
        DisplayName        = "GIIM $($role.Key)"
        Description        = $role.Value
        AllowedMemberTypes = @('User')   # people and groups
        IsEnabled          = $true
    }
}

$graphAppId = '00000003-0000-0000-c000-000000000000'
$settings = @{
    DisplayName            = $DisplayName
    SignInAudience         = 'AzureADMyOrg'   # this organisation only
    AppRoles               = $appRoles
    Web                    = @{
        RedirectUris          = @($o.signInRedirectUri.value)
        LogoutUrl             = $o.signOutRedirectUri.value
        HomePageUrl           = $o.myAppsHomePageUrl.value
        ImplicitGrantSettings = @{ EnableIdTokenIssuance = $false; EnableAccessTokenIssuance = $false }
    }
    OptionalClaims         = @{ IdToken = @(@{ Name = 'email'; Essential = $false }) }
    RequiredResourceAccess = @(@{
            ResourceAppId  = $graphAppId
            ResourceAccess = @(
                @{ Id = '37f7f235-527c-4136-accd-4a02d197296e'; Type = 'Scope' }   # openid
                @{ Id = '14dad69e-099b-42c9-810b-d002981feec1'; Type = 'Scope' }   # profile
                @{ Id = '64a6cdd6-aab1-4aaf-94b8-3cc8405e90d0'; Type = 'Scope' }   # email
            )
        })
}

if ($app) {
    Update-MgApplication -ApplicationId $app.Id -BodyParameter $settings
    Write-Host "Updated app registration '$DisplayName'."
}
else {
    $app = New-MgApplication -BodyParameter $settings
    Write-Host "Created app registration '$DisplayName'." -ForegroundColor Green
}
$app = Get-MgApplication -ApplicationId $app.Id

# Trust GIIM's managed identity as this app's credential (no client secret).
$issuer = "https://login.microsoftonline.com/$tenantId/v2.0"
$subject = $o.apiIdentityPrincipalId.value
$trusted = Get-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id | Where-Object { $_.Subject -eq $subject -and $_.Issuer -eq $issuer }
if (-not $trusted) {
    New-MgApplicationFederatedIdentityCredential -ApplicationId $app.Id -BodyParameter @{
        Name        = "giim-$Environment-api-managed-identity"
        Issuer      = $issuer
        Subject     = $subject
        Audiences   = @('api://AzureADTokenExchange')
        Description = 'GIIM web app managed identity: proves GIIM is GIIM when staff sign in'
    } | Out-Null
    Write-Host 'Trusted the GIIM managed identity as the app credential.' -ForegroundColor Green
}

# ---- The enterprise application (what appears in My Apps) -----------------------------------------------------------

$sp = Get-MgServicePrincipal -Filter "appId eq '$($app.AppId)'" | Select-Object -First 1
if (-not $sp) { $sp = New-MgServicePrincipal -AppId $app.AppId }
# Assignment required: only assigned people and groups can sign in or see the tile.
# The integrated-app tag (and no HideApp tag) makes the tile visible in My Apps.
Update-MgServicePrincipal -ServicePrincipalId $sp.Id -BodyParameter @{
    AppRoleAssignmentRequired = $true
    Tags                      = @('WindowsAzureActiveDirectoryIntegratedApp')
    Homepage                  = $o.myAppsHomePageUrl.value
}

# Admin consent for sign-in (openid, profile, email), so nobody sees a consent prompt.
$graph = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"
$grant = Get-MgOauth2PermissionGrant -Filter "clientId eq '$($sp.Id)' and resourceId eq '$($graph.Id)'" | Select-Object -First 1
if (-not $grant) {
    New-MgOauth2PermissionGrant -BodyParameter @{
        ClientId = $sp.Id; ConsentType = 'AllPrincipals'; ResourceId = $graph.Id; Scope = 'openid profile email'
    } | Out-Null
}

# ---- Who gets which role --------------------------------------------------------------------------------------------

$assignments = [ordered]@{
    Administrator = $AdministratorsGroup
    Technician    = $TechniciansGroup
    Manager       = $ManagersGroup
    Viewer        = $ViewersGroup
}
$current = Get-MgServicePrincipalAppRoleAssignedTo -ServicePrincipalId $sp.Id -All
foreach ($assignment in $assignments.GetEnumerator()) {
    if (-not $assignment.Value) { continue }
    $group = if ($assignment.Value -match '^[0-9a-f-]{36}$') { Get-MgGroup -GroupId $assignment.Value }
             else { Get-MgGroup -Filter "displayName eq '$($assignment.Value)'" | Select-Object -First 1 }
    if (-not $group) { Write-Warning "Group '$($assignment.Value)' not found; $($assignment.Key) not assigned."; continue }
    $role = $app.AppRoles | Where-Object { $_.Value -eq $assignment.Key }
    if ($current | Where-Object { $_.PrincipalId -eq $group.Id -and $_.AppRoleId -eq $role.Id }) { continue }
    New-MgServicePrincipalAppRoleAssignedTo -ServicePrincipalId $sp.Id -BodyParameter @{
        PrincipalId = $group.Id; ResourceId = $sp.Id; AppRoleId = $role.Id
    } | Out-Null
    Write-Host "$($group.DisplayName) -> $($assignment.Key)" -ForegroundColor Green
}

Write-Host ''
Write-Host "Done. Application (client) ID: $($app.AppId)" -ForegroundColor Green
Write-Host "Next: set entraClientId = '$($app.AppId)' in infra/params/$Environment.bicepparam and run ./infra/deploy.ps1 -Environment $Environment"
Write-Host 'Optional: add a logo in Entra admin centre > Enterprise applications > GIIM > Properties.'
