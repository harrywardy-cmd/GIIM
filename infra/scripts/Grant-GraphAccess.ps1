<#
.SYNOPSIS
    Lets GIIM's managed identities read Intune devices and the staff directory from Microsoft Graph (read-only).
    Safe to run again.

.DESCRIPTION
    Managed identities can't be given Graph permissions in the portal, so an Entra administrator who can grant
    admin consent (Privileged Role Administrator or Global Administrator) runs this once per environment.
    Needs the Microsoft Graph PowerShell SDK:  Install-Module Microsoft.Graph.Applications -Scope CurrentUser
    and `az login` (to look up the identities).

    Permissions granted (application, read-only):
      DeviceManagementManagedDevices.Read.All   Intune devices
      User.Read.All                             staff: names, departments, managers, employee IDs, hire dates
      User-LifeCycleInfo.Read.All               leave dates, only with -IncludeLeaveDates

.EXAMPLE
    ./infra/scripts/Grant-GraphAccess.ps1 -Environment prod
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('test', 'prod')]
    [string] $Environment,

    [string] $ResourceGroup = "rg-giim-$Environment",

    # Also read leave dates (set directoryReadLeaveDates = true in the .bicepparam file too).
    [switch] $IncludeLeaveDates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$permissions = @('DeviceManagementManagedDevices.Read.All', 'User.Read.All')
if ($IncludeLeaveDates) { $permissions += 'User-LifeCycleInfo.Read.All' }
$graphAppId = '00000003-0000-0000-c000-000000000000'   # Microsoft Graph

$o = az deployment group show --resource-group $ResourceGroup --name giim-main --query properties.outputs -o json | ConvertFrom-Json
if (-not $o) { throw "No GIIM deployment found in $ResourceGroup. Run infra/deploy.ps1 first." }

$identities = [ordered]@{
    $o.apiIdentityName.value     = $o.apiIdentityPrincipalId.value
    $o.workersIdentityName.value = $o.workersIdentityPrincipalId.value
}

Connect-MgGraph -TenantId $o.tenantId.value -Scopes 'AppRoleAssignment.ReadWrite.All', 'Application.Read.All' -NoWelcome
$graph = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"
foreach ($identity in $identities.GetEnumerator()) {
    $assigned = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $identity.Value -All |
        Where-Object { $_.ResourceId -eq $graph.Id }
    foreach ($permission in $permissions) {
        $role = $graph.AppRoles | Where-Object { $_.Value -eq $permission -and $_.AllowedMemberTypes -contains 'Application' }
        if (-not $role) { throw "Microsoft Graph has no application permission called $permission." }
        if ($assigned | Where-Object { $_.AppRoleId -eq $role.Id }) {
            Write-Host "$($identity.Key): already has $permission"
            continue
        }
        New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $identity.Value -PrincipalId $identity.Value `
            -ResourceId $graph.Id -AppRoleId $role.Id | Out-Null
        Write-Host "$($identity.Key): granted $permission" -ForegroundColor Green
    }
}
