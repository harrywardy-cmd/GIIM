# Connecting GIIM to Intune

GIIM reads device information from Intune through Microsoft Graph. It **only reads**: it never changes, wipes or retires devices.

It needs one Microsoft Graph **application** permission:

| Permission | Type | Why |
|---|---|---|
| `DeviceManagementManagedDevices.Read.All` | Application | Read device name, serial, model, OS, primary user, compliance state and last check-in |

GIIM does **not** need `DeviceManagementManagedDevices.ReadWrite.All` or `DeviceManagementManagedDevices.PrivilegedOperations.All`. Decline requests for either.

> **Scope:** application permissions apply to the whole tenant. Intune scope tags and RBAC roles do not limit app-only access, so GIIM will see every managed device.

There are two ways to grant this. Use **Option A** for testing from a developer PC, and **Option B** for the hosted app in Azure.

---

## Option A: App registration with a client secret (testing)

*Done by an Entra administrator (Application Administrator plus a role that can grant admin consent, e.g. Privileged Role Administrator or Global Administrator).*

1. **Entra admin center** → Identity → Applications → **App registrations** → **New registration**
   - Name: `GIIM - Intune read (test)`
   - Supported account types: **Accounts in this organizational directory only**
   - Redirect URI: leave blank
2. Open the new app → **API permissions** → **Add a permission** → **Microsoft Graph** → **Application permissions**
   → search `DeviceManagementManagedDevices.Read.All` → tick it → **Add permissions**.
3. Remove the default delegated `User.Read` permission (not needed).
4. Click **Grant admin consent for <tenant>** and confirm. The status column should show a green tick.
5. **Certificates & secrets** → **New client secret** → description `GIIM test`, expiry **90 days**. Copy the **Value** immediately; it is only shown once.
6. From **Overview**, note the **Application (client) ID** and **Directory (tenant) ID**.
7. Send the tenant ID, client ID and secret to the developer **through a password manager or Key Vault**, never by email or Teams.

When testing is finished, delete the client secret (or the whole app registration) so the secret can't be reused.

### Developer: configure the local API

Secrets go in .NET user-secrets, which are stored in your Windows profile, outside the repository:

```powershell
dotnet user-secrets init --project src/Giim.Api
dotnet user-secrets set "Intune:Source"       "Graph"            --project src/Giim.Api
dotnet user-secrets set "Intune:TenantId"     "<tenant-id>"      --project src/Giim.Api
dotnet user-secrets set "Intune:ClientId"     "<client-id>"      --project src/Giim.Api
dotnet user-secrets set "Intune:ClientSecret" "<secret-value>"   --project src/Giim.Api
```

Then start the API, open **Intune reconciliation** and click **Sync now**. To switch back to the fake data, run `dotnet user-secrets remove "Intune:Source" --project src/Giim.Api`.

---

## Option B: Managed identity (production, recommended)

In Azure, GIIM runs as an App Service with a **system-assigned managed identity**. Azure manages its credentials automatically, so **there is no secret to store, rotate or leak**. GIIM uses it automatically when no client secret is configured.

Managed identities can't be granted Graph application permissions in the portal, so an administrator runs this once in PowerShell (Microsoft Graph PowerShell SDK):

```powershell
Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All", "Application.Read.All"

# Microsoft Graph's service principal and the read-only Intune device role
$graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
$role  = $graph.AppRoles | Where-Object {
    $_.Value -eq "DeviceManagementManagedDevices.Read.All" -and $_.AllowedMemberTypes -contains "Application" }

# The App Service's managed identity (same name as the App Service)
$giim  = Get-MgServicePrincipal -Filter "displayName eq '<app-service-name>'"

New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $giim.Id `
    -PrincipalId $giim.Id -ResourceId $graph.Id -AppRoleId $role.Id
```

Check it in **Entra admin center** → Enterprise applications → (filter: Managed Identities) → `<app-service-name>` → **Permissions**.

App configuration (App Service → Environment variables):

| Setting | Value |
|---|---|
| `Intune__Source` | `Graph` |
| `Intune__SyncInterval` | `04:00:00` (every 4 hours) |

---

## How GIIM calls Graph

- `GET /v1.0/deviceManagement/managedDevices?$select=id,deviceName,serialNumber,manufacturer,model,operatingSystem,userPrincipalName,complianceState,lastSyncDateTime,enrolledDateTime`
- It follows `@odata.nextLink` until every page is read. The endpoint has no delta query, so each sync is a full read of these fields only.
- If Graph throttles (HTTP 429) or is briefly unavailable (503/504), GIIM waits for the time Graph asks (`Retry-After`) and retries up to 5 times.
- Each run is recorded in GIIM (**Intune reconciliation** page → last sync; API `GET /api/intune/sync-runs`), and every sign-in by the app appears in Entra **Sign-in logs → Service principal sign-ins**.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Sync fails with **403 Forbidden** | Admin consent not granted, or the permission was added as *Delegated* instead of *Application* |
| Sync fails with **401 Unauthorized** | Wrong tenant/client ID, or the client secret expired |
| `AADSTS7000215: Invalid client secret` | The secret **ID** was copied instead of the secret **Value** |
| Sync works but devices have no serial | Some virtual machines and older hardware report placeholder serials; GIIM lists these as "Not in register" with no serial |
