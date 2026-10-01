# GIIM in Azure

Everything GIIM needs in Azure is defined here as Bicep templates, so an environment can be created, checked and
recreated the same way every time. There are two environments, **test** and **prod**, each in its own resource
group in **Australia East**. Code is deployed by GitHub Actions; this folder only builds the platform it runs on.

| File | What it is |
|---|---|
| `main.bicep`, `modules/` | The templates |
| `params/test.bicepparam`, `params/prod.bicepparam` | Settings for each environment (no secrets) |
| `deploy.ps1` | Creates or updates an environment, after showing what will change |
| `scripts/Grant-SqlAccess.ps1`, `sql/grant-access.sql` | One-off: gives GIIM's identities access to the database |
| `scripts/Grant-GraphAccess.ps1` | One-off: lets GIIM read Intune devices and the staff directory |
| `../.github/workflows/` | CI on every change; deployment to test, then prod after approval |

## What gets created

```
Staff browser ──HTTPS──► App Service: web app (UI + API) ──┐        GitHub Actions
                                                           │        (deploys code, runs DB migrations)
                         App Service: workers (syncs) ─────┤
                                                           │ private network
                  ┌────────────────────┬───────────────────┼──────────────────┐
                  ▼                    ▼                   ▼                  ▼
            Azure SQL (giim)      Key Vault          Blob Storage       NAT gateway ──► Microsoft Graph,
            Entra sign-in only    cookie key,        cookie keys,       (one fixed       ServiceDesk Plus
            audited, backed up    cookie key         attachments        outbound IP)
```

| Resource | Test | Prod |
|---|---|---|
| App Service plan (Linux) | B1 | P0v3 |
| Azure SQL database | S0 (10 DTU), 7-day restore | S2 (50 DTU), 14-day restore, weekly/monthly/yearly backups kept 5 weeks / 12 months / 7 years, copied to Australia Southeast |
| Storage | LRS | ZRS (3 copies across zones) |
| Key Vault | purge protection off | purge protection on |
| Log Analytics + Application Insights | 30 days | 90 days |
| Private network, private endpoints, NAT gateway | yes | yes |

**Rough monthly cost** (Australia East, AUD, before tax; confirm in the
[Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/)): **prod about $320-380**, **test about
$130-160**. The biggest items are the App Service plan, the database, the NAT gateway (~$55) and the three private
endpoints (~$35). Test can be deleted when not needed and recreated with `deploy.ps1` in about 15 minutes.

### Security design

- **No passwords anywhere.** The web app, the workers and the GitHub pipeline each sign in with their own managed
  identity. SQL accepts Microsoft Entra sign-in only; storage has account keys disabled; FTP and basic-auth
  deployment are off. GitHub signs in with a short-lived token that Azure trusts only for this repository's
  `test` or `prod` GitHub environment.
- **Private by default.** SQL, Key Vault and Blob Storage are reachable only from GIIM's private network. The
  database's public endpoint has no firewall rules; the pipeline opens it to its own address while migrations run
  and always closes it. The workers accept no traffic from the internet.
- **Least privilege.** The apps can read and write data but the database refuses to edit or delete history
  (asset timeline, audit log, stock ledger), even if the app were compromised. Only the deployment identity can
  change the schema. The Intune permission is read-only.
- **Uploaded files** (asset photos and documents) are checked by type and content before they are stored, kept
  under names GIIM chooses in a container only the web app can reach, never overwritten, and served with headers
  that stop a browser running anything inside them.
- **Audited.** Every SQL sign-in and query, every Key Vault access and every storage access is logged to
  Log Analytics, alongside the app's own logs and the HTTP logs.
- **Browser protections.** HTTPS only (HSTS), HTTPS-only sign-in cookie, strict Content Security Policy, no framing.

---

## First deployment

Allow about an hour. Steps 1-4 need someone with **Owner** on the subscription (or Contributor + User Access
Administrator); step 5 needs an Entra admin who can grant admin consent.

### 1. Prepare

1. Install the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows) and sqlcmd:
   `winget install Microsoft.AzureCLI` and `winget install sqlcmd`.
2. In Entra, create a security group for database administrators, e.g. **GIIM-SQL-Admins**, add yourself and one
   other person, and copy its **Object ID**.
3. Fill in the `FILL IN` values in `params/test.bicepparam` and `params/prod.bicepparam`: the group name and
   Object ID, and alert email addresses. Microsoft sign-in and the custom domain can wait until later.

### 2. Preview, then deploy

```powershell
az login
az account set --subscription "<subscription name or id>"
./infra/deploy.ps1 -Environment test -WhatIf     # shows what would be created; changes nothing
./infra/deploy.ps1 -Environment test             # shows the changes again and asks before making them
```

At the end it prints the values for GitHub (step 4), the addresses for the Entra app registration (step 6) and GIIM's outbound
IP address.

### 3. Give GIIM access to its database

Run as a member of GIIM-SQL-Admins:

```powershell
./infra/scripts/Grant-SqlAccess.ps1 -Environment test
```

### 4. Connect GitHub

In GitHub → the GIIM repository → **Settings**:

1. **Environments** → create `test` and `prod`.
   - On `prod`: tick **Required reviewers** (add yourself and a colleague) and, under **Deployment branches**,
     allow only `main`.
   - On each, add the **environment variables** printed by `deploy.ps1` (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
     `AZURE_SUBSCRIPTION_ID`, `RESOURCE_GROUP`, `API_APP_NAME`, `WORKERS_APP_NAME`, `SQL_SERVER_NAME`,
     `SQL_SERVER_FQDN`, `DATABASE_NAME`, `PUBLIC_URL`). None of these are secrets.
2. **Secrets and variables → Actions → Variables** → add the repository variable `DEPLOY_ENABLED` = `true`.
3. **Actions** → **Deploy** → **Run workflow**. It builds, tests, updates the database, deploys, checks
   `/health`, and then waits for approval before production.

### 5. Let GIIM read Intune and the staff directory

An Entra admin (Privileged Role Administrator or Global Administrator) runs:

```powershell
Install-Module Microsoft.Graph.Applications -Scope CurrentUser   # first time only
./infra/scripts/Grant-GraphAccess.ps1 -Environment test
```

This grants read-only access to Intune devices and to user accounts (names, departments, managers, employee IDs).
GIIM then syncs the staff list from Entra every 4 hours; see [docs/staff-directory.md](../docs/staff-directory.md).

### 6. Microsoft sign-in and the My Apps tile

An Entra administrator runs `./infra/scripts/New-GiimAppRegistration.ps1 -Environment test` (or follows the manual
steps in [docs/entra-setup.md](../docs/entra-setup.md)); test and prod use separate app registrations. It prints the
**client ID**:

1. Put it in the environment's `.bicepparam` file (`entraClientId`).
2. Run `./infra/deploy.ps1 -Environment test` again.

There is no client secret: the app registration trusts GIIM's managed identity instead.

### 7. Your own address (optional, recommended for prod)

1. App Service (the web app) → **Custom domains** → add e.g. `giim.company.com.au`, create the DNS records it
   asks for, then **Add binding** with a free App Service managed certificate.
2. Set `publicBaseUrl` in `params/prod.bicepparam`, run `deploy.ps1` again, and run
   `New-GiimAppRegistration.ps1` again so the sign-in addresses and the My Apps tile match.

Do this **before printing asset labels**: QR codes contain this address.

### 8. ServiceDesk Plus (when its administrator is ready)

Follow [docs/servicedesk-setup.md](../docs/servicedesk-setup.md): set `serviceDeskMode = 'Api'` and the client ID in the
`.bicepparam` file, then run `./infra/deploy.ps1 -Environment prod -SetServiceDeskSecrets`.

Repeat steps 2-6 with `-Environment prod` when test looks right.

---

## Day to day

| Task | How |
|---|---|
| Release a change | Merge to `main`. It deploys to test automatically; approve the prod deployment in GitHub → Actions. |
| Change infrastructure | Edit the templates or `.bicepparam`, then `./infra/deploy.ps1 -Environment test` (then prod). |
| Give someone access to GIIM | Add them to the right group (e.g. GIIM-Technicians), or assign them a role in Entra → Enterprise applications → GIIM → Users and groups |
| Scale up | Change `appServicePlanSku` or `sqlSku` in the `.bicepparam` and deploy. Both scale without downtime (a brief reconnect). |
| Look at logs | Application Insights → **Logs**, e.g. `traces \| where timestamp > ago(1d) \| order by timestamp desc`. Failures: **Failures** blade. |
| Alerts | Emailed to `alertEmails`: server errors, health checks failing, database over 80% full or 90% busy, Intune or staff directory sync failures. |
| Someone is missing from GIIM | Check their employee ID, department and manager in AD; see [docs/staff-directory.md](../docs/staff-directory.md) |

### Restoring the database

Point-in-time restore creates a **new** database next to the old one, so nothing is overwritten:

```powershell
az sql db restore -g rg-giim-prod -s <sql server name> -n giim --dest-name giim-restored `
    --time "2026-10-01T09:30:00+10:00"
```

Then either copy the needed rows across, or (for a full rollback) rename the databases in the portal so the
restored copy is called `giim` and run `Grant-SqlAccess.ps1` again. Long-term backups are under the SQL server →
**Backups** → **Available backups**.

### Emergency database access

Members of GIIM-SQL-Admins can connect with Entra sign-in (e.g. SQL Server Management Studio, "Microsoft Entra
MFA") after adding their IP address under SQL server → **Networking**. Remove the rule afterwards.

---

## Not included yet

| Item | When |
|---|---|
| Service Bus (queues for the on-premises AD/Exchange agent and SDP webhooks) | Phase 2-3, with the agent |
| Standby database in Australia Southeast (geo-replica) | If a regional outage must be recovered in minutes rather than hours (backups are already copied there) |
| Staging slot (zero-downtime deployments) | If the few seconds of restart during a deployment become a problem |
| Web application firewall (Front Door) | If GIIM must be reachable from anywhere and attracts attack traffic; `allowedIpRanges` can restrict it to office/VPN addresses meanwhile |
| Microsoft Defender for SQL / App Service | Recommended if your organisation already uses Defender for Cloud |
| Malware scanning of uploaded files (Defender for Storage) | Recommended with Defender: scans each asset file as it arrives; charged per GB scanned (small for photos and PDFs, check current pricing) |
