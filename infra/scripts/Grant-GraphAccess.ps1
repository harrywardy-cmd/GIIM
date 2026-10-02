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
      Group.Read.All                            look up cloud-only groups by name, only with -IncludeCloudGroups

    With -IncludeCloudGroups the workers' identity also gets the Groups Administrator role, but only over the
    administrative unit 'GIIM managed groups' (created if missing): it can add people to the groups you put in that
    unit and to no other group. Add the cloud-only groups that starter profiles use to the unit in Entra admin centre.

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
    [switch] $IncludeLeaveDates,

    # Let starter automation add people to cloud-only Entra groups (those in the administrative unit below).
    [switch] $IncludeCloudGroups,

    [string] $AdministrativeUnit = 'GIIM managed groups'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$permissions = @('DeviceManagementManagedDevices.Read.All', 'User.Read.All')
if ($IncludeLeaveDates) { $permissions += 'User-LifeCycleInfo.Read.All' }
if ($IncludeCloudGroups) { $permissions += 'Group.Read.All' }
$graphAppId = '00000003-0000-0000-c000-000000000000'   # Microsoft Graph

$o = az deployment group show --resource-group $ResourceGroup --name giim-main --query properties.outputs -o json | ConvertFrom-Json
if (-not $o) { throw "No GIIM deployment found in $ResourceGroup. Run infra/deploy.ps1 first." }

$identities = [ordered]@{
    $o.apiIdentityName.value     = $o.apiIdentityPrincipalId.value
    $o.workersIdentityName.value = $o.workersIdentityPrincipalId.value
}

$scopes = @('AppRoleAssignment.ReadWrite.All', 'Application.Read.All')
if ($IncludeCloudGroups) { $scopes += @('AdministrativeUnit.ReadWrite.All', 'RoleManagement.ReadWrite.Directory') }
Connect-MgGraph -TenantId $o.tenantId.value -Scopes $scopes -NoWelcome
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

# ---- Cloud-only groups: Groups Administrator over one administrative unit only -------------------------------------

if ($IncludeCloudGroups) {
    $unit = Get-MgDirectoryAdministrativeUnit -Filter "displayName eq '$AdministrativeUnit'" | Select-Object -First 1
    if (-not $unit) {
        $unit = New-MgDirectoryAdministrativeUnit -BodyParameter @{
            DisplayName = $AdministrativeUnit
            Description = 'Cloud-only groups GIIM may add starters to. Only groups put here can be changed by GIIM.'
        }
        Write-Host "Created the administrative unit '$AdministrativeUnit'." -ForegroundColor Green
    }
    $groupsAdministrator = 'fdd7a751-b60b-444a-984c-02652fe8fa1c'   # Groups Administrator (built-in role)
    $scope = "/administrativeUnits/$($unit.Id)"
    $workers = $o.workersIdentityPrincipalId.value
    $assigned = Get-MgRoleManagementDirectoryRoleAssignment -Filter "principalId eq '$workers'" -All |
        Where-Object { $_.RoleDefinitionId -eq $groupsAdministrator -and $_.DirectoryScopeId -eq $scope }
    if ($assigned) {
        Write-Host "$($o.workersIdentityName.value): already Groups Administrator over '$AdministrativeUnit'"
    }
    else {
        New-MgRoleManagementDirectoryRoleAssignment -BodyParameter @{
            PrincipalId = $workers; RoleDefinitionId = $groupsAdministrator; DirectoryScopeId = $scope
        } | Out-Null
        Write-Host "$($o.workersIdentityName.value): Groups Administrator over '$AdministrativeUnit' only" -ForegroundColor Green
    }
    Write-Host "Next: in Entra admin centre > Administrative units > $AdministrativeUnit > Groups, add the cloud-only groups starter profiles use."
}
