# Signing in to GIIM with Microsoft (My Apps)

Staff sign in to GIIM with their Microsoft work account, usually by clicking the **GIIM tile in My Apps**
(myapps.microsoft.com). Because they are already signed in to Microsoft, GIIM opens without asking for a password.
GIIM has no passwords of its own, and your Entra MFA and Conditional Access policies apply as usual.

What each person can do comes from the **GIIM app role** assigned to them, directly or (normally) through a group.

## Roles

| App role | Can |
|---|---|
| Administrator | Everything, including locations, categories, register import and directory sync |
| Technician | Add, assign, return, repair, retire, move and label assets; stock; process device requests (order, receive, hand over) |
| Manager | Read everything; raise device requests and approve or reject those they are the approver for (usually their own team's) |
| Viewer | Read everything |

Someone who can sign in but has no role sees a "no access" page. With **assignment required** switched on (the
script below does this), people who aren't assigned can't sign in at all and don't see the tile.

**Approvers:** GIIM recognises a manager as the approver of their team's device requests by matching their sign-in
to their record in GIIM's staff directory: by Entra object ID once the directory sync records it, otherwise by their
sign-in name (UPN) or email. With accounts synced from Active Directory these match already.

---

## For the Entra administrator

Allow about 20 minutes, after the Azure environment exists (`infra/deploy.ps1` prints the addresses below).

### Option A: run the script (recommended)

```powershell
Install-Module Microsoft.Graph -Scope CurrentUser   # first time only
az login
./infra/scripts/New-GiimAppRegistration.ps1 -Environment prod `
    -AdministratorsGroup 'GIIM-Administrators' -TechniciansGroup 'GIIM-Technicians' `
    -ManagersGroup 'GIIM-Managers' -ViewersGroup 'GIIM-Viewers'
```

It needs **Application Administrator** (or Cloud Application Administrator plus permission to grant admin consent).
It is safe to run again. Create the four groups first (in Active Directory if you manage groups there, and let
them sync), or leave the group parameters out and assign people later in the portal.

Then put the **client ID** it prints into `infra/params/prod.bicepparam` (`entraClientId`) and run
`./infra/deploy.ps1 -Environment prod`. Use a separate registration for test (`-Environment test`).

### Option B: set it up in the Entra admin centre

1. **App registrations → New registration**: name `GIIM`, **Accounts in this organisational directory only**,
   redirect URI platform **Web** = `https://<giim-address>/signin-oidc`.
2. **Authentication**: front-channel logout URL `https://<giim-address>/signout-callback-oidc`. Leave both implicit
   grant boxes **unticked**.
3. **App roles → Create app role**, four times: allowed member types **Users/Groups**, values exactly
   `Administrator`, `Technician`, `Manager`, `Viewer` (GIIM matches these names).
4. **Token configuration → Add optional claim**: ID token, `email`.
5. **API permissions**: Microsoft Graph delegated `openid`, `profile`, `email`, then **Grant admin consent**.
6. **Certificates & secrets → Federated credentials → Add credential → Managed identity**: choose the managed
   identity `id-giim-prod-api` (in the GIIM resource group). This lets GIIM prove who it is without a client secret.
   Leave **Client secrets** empty.
7. **Enterprise applications → GIIM → Properties**: **Assignment required** = Yes, **Visible to users** = Yes,
   home page URL `https://<giim-address>/auth/login` (the tile then starts sign-in straight away). Add a logo if you like.
8. **Enterprise applications → GIIM → Users and groups → Add user/group**: assign each group its role.
9. Send the developer the **Application (client) ID** and **Directory (tenant) ID**. Neither is a secret.

> Assigning **groups** to an app needs Microsoft Entra ID P1 (included in Microsoft 365 E3/E5 and Business
> Premium). Without P1, assign people individually.

---

## For the developer: configuration

| Setting | Value |
|---|---|
| `Auth:Mode` | `Entra` (everywhere except a developer PC). `Development` gives a pick-a-role sign-in and **only works in the Development environment**; GIIM refuses to start with it anywhere else |
| `Auth:Entra:TenantId` | Directory (tenant) ID. In Azure, set from the subscription's tenant |
| `Auth:Entra:ClientId` | Application (client) ID. Until it is set, GIIM runs and the sign-in page says sign-in isn't set up |
| `Auth:Entra:ManagedIdentityClientId` | In Azure: the web app's managed identity, trusted by the app registration (set by the deployment) |
| `Auth:Entra:ClientSecret` | Only for testing real Microsoft sign-in from a developer PC (user-secrets). Never in Azure |
| `Auth:SessionIdleTimeout` | Default `08:00:00` |

Day-to-day development uses the pick-a-role sign-in, so none of this is needed to work on GIIM. To test real
Microsoft sign-in from a developer PC:

- Use the **test** app registration with an extra redirect URI `https://localhost:7274/signin-oidc` and a
  short-lived client secret (a managed identity only exists in Azure). Never add localhost or a secret to the
  production registration.
- It must be **HTTPS**: Microsoft returns to GIIM with a form post, and browsers only keep the sign-in cookies that
  round trip needs over HTTPS. Run the API with the `https` launch profile and open `https://localhost:7274` (build the
  UI into the API first: `npm run build` in `src/Giim.Web`, then copy `dist` to `src/Giim.Api/wwwroot`).

```powershell
dotnet user-secrets init --project src/Giim.Api
dotnet user-secrets set "Auth:Mode"               "Entra"       --project src/Giim.Api
dotnet user-secrets set "Auth:Entra:TenantId"     "<tenant-id>" --project src/Giim.Api
dotnet user-secrets set "Auth:Entra:ClientId"     "<client-id>" --project src/Giim.Api
dotnet user-secrets set "Auth:Entra:ClientSecret" "<secret>"    --project src/Giim.Api
dotnet run --project src/Giim.Api --launch-profile https
```

Remove the secret afterwards (`dotnet user-secrets clear --project src/Giim.Api`) and delete it in Entra.

## How it works (for reviewers)

- The API performs the OpenID Connect sign-in (authorization code with PKCE) against this organisation's tenant
  only, and keeps the session in an encrypted, HTTP-only cookie. **No tokens are exposed to the browser**, so a
  script injected into the page can't steal them.
- In Azure the app registration has **no client secret**: GIIM exchanges a token from its managed identity instead
  (a federated identity credential), so there is nothing to leak, store or renew.
- Roles come from the `roles` claim (app roles). Group names aren't used, so there is no limit on how many groups
  someone belongs to.
- Every `/api` endpoint requires a GIIM role. **Any request that changes data requires Technician or Administrator**,
  applied automatically to all endpoints (including future ones) and checked by an automated test. The exceptions
  are device-request actions managers take, marked explicitly. Set-up changes require Administrator (the list is in
  `AuthSetup.AdministratorOnly`).
- Changes must carry an `X-GIIM-Request` header. Other websites can't add custom headers to requests they trigger,
  which blocks cross-site request forgery.
- Signing out ends the GIIM session and the Microsoft session in that browser, so a shared PC is left signed out.
- Every timeline entry, stock movement and audit entry records the signed-in user's sign-in name (UPN).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `AADSTS50105`: not assigned to the application | The person (or their group) has no GIIM role assignment |
| Signed in, but "No access to GIIM" | Assigned without a role (the "Default Access" option), or the role values don't match the four names exactly |
| `AADSTS50011`: redirect URI mismatch | The redirect URI doesn't match GIIM's address exactly (https, host, `/signin-oidc`) |
| `AADSTS700213` / `AADSTS70021`: no matching federated identity | The federated credential doesn't point at the `id-giim-<env>-api` managed identity |
| A manager can't see their team's requests under Approvals | Their staff record's UPN/email doesn't match their sign-in name |
