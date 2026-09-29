<#
.SYNOPSIS
    Lets GIIM's managed identities read Intune devices from Microsoft Graph (read-only). Safe to run again.

.DESCRIPTION
    Managed identities can't be given Graph permissions in the portal, so an Entra administrator who can grant
    admin consent (Privileged Role Administrator or Global Administrator) runs this once per environment.
    Needs the Microsoft Graph PowerShell SDK:  Install-Module Microsoft.Graph.Applications -Scope CurrentUser
    and `az login` (to look up the identities).
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

$permission = 'DeviceManagementManagedDevices.Read.All'
$graphAppId = '00000003-0000-0000-c000-000000000000'   # Microsoft Graph

$o = az deployment group show --resource-group $ResourceGroup --name giim-main --query properties.outputs -o json | ConvertFrom-Json
if (-not $o) { throw "No GIIM deployment found in $ResourceGroup. Run infra/deploy.ps1 first." }

$identities = [ordered]@{
    $o.apiIdentityName.value     = $o.apiIdentityPrincipalId.value
    $o.workersIdentityName.value = $o.workersIdentityPrincipalId.value
}

Connect-MgGraph -TenantId $o.tenantId.value -Scopes 'AppRoleAssignment.ReadWrite.All', 'Application.Read.All' -NoWelcome
$graph = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"
$role = $graph.AppRoles | Where-Object { $_.Value -eq $permission -and $_.AllowedMemberTypes -contains 'Application' }

foreach ($identity in $identities.GetEnumerator()) {
    $existing = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $identity.Value |
        Where-Object { $_.AppRoleId -eq $role.Id -and $_.ResourceId -eq $graph.Id }
    if ($existing) {
        Write-Host "$($identity.Key): already has $permission"
        continue
    }
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $identity.Value -PrincipalId $identity.Value `
        -ResourceId $graph.Id -AppRoleId $role.Id | Out-Null
    Write-Host "$($identity.Key): granted $permission" -ForegroundColor Green
}
