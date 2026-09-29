# Signing in to GIIM with Okta

Staff sign in to GIIM with Okta (OpenID Connect, authorization code flow with PKCE). GIIM has no passwords of its
own, and Okta's MFA and sign-on policies apply as usual. What each person can do comes from **Okta group membership**.

## Roles

| Okta group (default name) | GIIM role | Can |
|---|---|---|
| `GIIM-Administrators` | Administrator | Everything, including locations, categories, register import and directory sync |
| `GIIM-Technicians` | Technician | Add, assign, return, repair, retire, move and label assets; stock; resolve spreadsheet owners; Intune sync |
| `GIIM-Managers` | Manager/Approver | Read everything (team views and approvals arrive with device requests in Phase 2) |
| `GIIM-Viewers` | Viewer | Read everything |

Someone signed in to Okta but in **none** of these groups sees a "no access" page. Group names can be changed in
GIIM's settings (`Auth:RoleGroups`) if your naming convention differs.

---

## For the Okta administrator

1. **Applications → Create App Integration** → **OIDC – OpenID Connect** → **Web Application**.
   - Name: `GIIM`
   - Grant type: **Authorization Code** only (no implicit, no refresh token needed)
   - Sign-in redirect URI: `https://<giim-address>/signin-oidc`
   - Sign-out redirect URI: `https://<giim-address>/signout-callback-oidc`
   - Assignments: limit access to the four GIIM groups below
   - For testing on a developer PC, also add `http://localhost:5173/signin-oidc` and `http://localhost:5173/signout-callback-oidc`
     to a **separate test app** (don't add localhost to the production app).
2. **Directory → Groups**: create `GIIM-Administrators`, `GIIM-Technicians`, `GIIM-Managers`, `GIIM-Viewers` and add people.
3. **Groups claim**: GIIM reads the user's groups from a `groups` claim.
   - Org authorization server: in the app's **Sign On** tab → *OpenID Connect ID Token* → Groups claim type **Filter**,
     name `groups`, **Starts with** `GIIM-`.
   - Custom authorization server (e.g. `default`): **Security → API → Authorization Servers → Claims** → add claim `groups`,
     value type **Groups**, filter **Starts with** `GIIM-`, include in **ID Token** and **Userinfo**.
   Filtering to `GIIM-` keeps the sign-in small and doesn't disclose unrelated group memberships.
4. Send the developer, **via a password manager or Key Vault (never email or Teams)**:
   - the **issuer / authority** (e.g. `https://company.okta.com` or `https://company.okta.com/oauth2/default`),
   - the **client ID**,
   - the **client secret**.

---

## For the developer: configuration

| Setting | Value |
|---|---|
| `Auth:Mode` | `Okta` (production). `Development` gives a pick-a-role sign-in and **only works in the Development environment**; GIIM refuses to start with it anywhere else |
| `Auth:Okta:Authority` | Okta issuer from step 4 |
| `Auth:Okta:ClientId` | Client ID |
| `Auth:Okta:ClientSecret` | Client secret: `dotnet user-secrets` locally, Key Vault in Azure |
| `Auth:RoleGroups:*` | Only if your group names differ from the defaults |
| `Auth:SessionIdleTimeout` | Default `08:00:00` |

Testing the real Okta sign-in locally:

```powershell
dotnet user-secrets init --project src/Giim.Api
dotnet user-secrets set "Auth:Mode"              "Okta"            --project src/Giim.Api
dotnet user-secrets set "Auth:Okta:Authority"    "<issuer>"        --project src/Giim.Api
dotnet user-secrets set "Auth:Okta:ClientId"     "<client-id>"     --project src/Giim.Api
dotnet user-secrets set "Auth:Okta:ClientSecret" "<client-secret>" --project src/Giim.Api
```

## Production checklist (Azure App Service)

- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so GIIM sees the original **https** address behind App Service and
  sends Okta the right callback URL.
- **Persist the Data Protection keys** (Blob Storage, protected with Key Vault). They encrypt the sign-in cookie;
  without them everyone is signed out whenever the app restarts or scales out.
- HTTPS only. The session cookie is `HttpOnly` and `SameSite=Lax`, and is marked `Secure` on HTTPS.

## How it works (for reviewers)

- The API performs the OIDC sign-in and keeps the session in an encrypted, HTTP-only cookie. **No tokens are exposed
  to the browser**, so a script injected into the page cannot steal them.
- Every `/api` endpoint requires a GIIM role. **Any request that changes data requires Technician or Administrator**,
  applied automatically to all endpoints (including future ones) and checked by an automated test. Set-up changes
  require Administrator; the list is in `AuthSetup.AdministratorOnly`.
- Changes must carry an `X-GIIM-Request` header. Other websites can't add custom headers to requests they trigger,
  which blocks cross-site request forgery.
- Every timeline entry, stock movement and audit entry records the signed-in user's Okta username.
