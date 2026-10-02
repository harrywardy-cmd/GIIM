# The on-prem agent (starter automation)

GIIM can carry out a starter checklist's account steps automatically: create the AD account, enable the hybrid
mailbox, add the groups from the starter profile, and enable the account on the start date. The AD and Exchange steps
are done by the **GIIM agent**, a small Windows service on a domain-joined server; GIIM itself waits for the account
to reach Entra ID and emails the manager.

> **Status:** GIIM's side, the agent program and a **stand-in directory** are built and tested, so the whole flow runs
> on a developer PC. The agent's **real AD and Exchange steps aren't built yet**: they wait for the AD and Exchange
> administrators' decisions below. Until then the steps are done by hand and ticked off, as now.

## How it works

```mermaid
sequenceDiagram
  participant T as Technician
  participant G as GIIM (Azure)
  participant A as GIIM agent (on-prem server)
  participant AD as Active Directory / Exchange
  participant E as Entra ID
  T->>G: Run automated steps (after a preview)
  G->>G: Queue jobs in order: account → mailbox, groups, cloud-sync wait → enable on start date → welcome email
  loop every 15 seconds (outbound HTTPS only)
    A->>G: Check in, take due jobs
    A->>AD: Create account (disabled) / mailbox / groups / enable
    A->>G: Report result (names and GUID, never a password)
  end
  G->>E: Is the account there yet? (Graph, read-only)
  G->>G: Tick each checklist task off; email the manager on the start date
```

- **Outbound only.** The agent connects out to GIIM over HTTPS. No inbound firewall rules, no VPN, no Service Bus.
- **A technician starts it**, per checklist, after seeing exactly what will happen. Steps run in order; the account is
  created **disabled** and enabled at 06:00 on the start date.
- **Dry run first.** With `Automation:DryRun` on (the default), every step only reports what it *would* do and the
  tasks go back to a person, with the plan in the task notes. Turn it off when the AD administrators are happy.
- **Safe to repeat.** Every step finds the account by **employee ID** first, so a step run twice (after a lost
  connection, say) does no harm. A job is held for 10 minutes; if the agent stops responding, the job is handed out again.
- **Failures stop for a person.** A temporary failure (a domain controller didn't answer) is retried after 1, 5 and
  15 minutes; anything else (a group that doesn't exist) stops at once. The task shows why; a technician **Retries** it
  after fixing the cause, or **Stops** it and does it by hand. Failures appear in the bell and under **Setup → Automation**.
- **No passwords in GIIM.** The agent creates the account with a long random password nobody is told. On their first
  morning, IT gives the starter a **Temporary Access Pass** from Entra to sign in and set up passwordless sign-in or
  a password of their own.

### Who does what

| Step | Done by | Ticks off |
|---|---|---|
| Create AD account | Agent | When created (GIIM records the sign-in name and AD object GUID) |
| Wait for Entra Connect | GIIM (Graph, every 2 minutes, up to 24 hours) | When the account is in Entra ID |
| Enable remote mailbox | Agent | When enabled (GIIM records the email address) |
| Add to group (security, licence, app access) | Agent | When added |
| Add to a cloud-only Entra group (ticked *cloud-only* on the profile item) | GIIM through Graph, after the cloud sync | When added |
| Enable the account | Agent, at 06:00 on the start date | When enabled |
| Send welcome email to manager | GIIM, after the account is enabled | When queued |

Leaver steps (disable, convert the mailbox, remove access) are Phase 4 and stay manual for now.

## Security

The agent can create accounts, so it is treated as a privileged system.

- **Its own identity, no secrets.** In Azure the agent signs in with its own Entra app registration and a
  **certificate** whose private key can't be exported from its server. GIIM accepts only tokens carrying the
  `Giim.Agent` app role from that app, on the `/agent` endpoints only. A staff session can't call them, and the agent
  can't call anything else. (On a developer PC a long shared key is used instead.)
- **Least privilege in AD.** Run the service as a **group managed service account** (gMSA) with only: create user
  objects in the starter OUs, write the attributes it sets, add members to the groups it may add, and Exchange
  `Recipient Management` (or a custom role with `Enable-RemoteMailbox` only). No Domain Admin rights.
- **Group allow-list on the agent.** `Accounts:AllowedGroups` (e.g. `APP-*`, `LIC-*`) lists the only groups it will
  add people to; privileged built-in groups (Domain Admins and the like) are refused whatever GIIM asks. So even a
  compromised GIIM couldn't use the agent to grant admin rights.
- **AD rules live with AD.** Username format, OUs and the routing domain are in the agent's settings on its server,
  not in GIIM.
- **Audited.** Every job records what was asked, by whom, the result and a short log; the agent also writes to the
  Windows event log. Jobs are kept with the checklist.
- **Rate-limited.** The `/agent` endpoints accept at most 300 requests a minute.

## Decisions for the AD and Exchange administrators

Needed before the real AD and Exchange steps are built:

- [ ] **Sign-in name format** (e.g. `first.last`, `flast`) and how clashes are numbered (`first.last2`).
- [ ] **UPN and email suffix.**
- [ ] **OU** for new starters, per department if needed.
- [ ] **Groups** the agent may add (`AllowedGroups`), and confirmation the starter profiles use those exact AD group names.
- [ ] **Remote mailbox**: routing domain (`tenant.mail.onmicrosoft.com`) and any address policy.
- [ ] **Service account**: gMSA name, and the delegated rights above.
- [ ] **Server**: which domain-joined server runs the agent (Windows Server 2019 or later, ActiveDirectory module and
  Exchange Management Tools installed), and that it can reach GIIM's address on 443.
- [ ] **First-day sign-in**: Temporary Access Pass (recommended) or another process.
- [ ] **Attributes** to set beyond name, employee ID, department, title, office and manager (e.g. company, cost centre).

### Cloud-only groups

Groups made in Entra ID (not synced from AD, e.g. a Teams team's group) can't be changed by the agent. Tick
**Cloud-only group (Entra)** on the starter profile item, and GIIM adds the new account through Graph once Entra Connect
has synced it. GIIM's workers get the **Groups Administrator** role over one **administrative unit**
("GIIM managed groups") only, so they can change the groups you put in that unit and no others; groups synced from AD,
groups that grant admin roles and dynamic groups are refused regardless. Set up with
`./infra/scripts/Grant-GraphAccess.ps1 -Environment prod -IncludeCloudGroups`, then add the groups to the unit.

### Starting automatically

With `Automation:StartFromServiceDesk` (`automationStartFromServiceDesk` in Azure; off by default), a starter's
automation starts as soon as their ServiceDesk Plus ticket creates the checklist, without waiting for a technician.
Turn it on once the automation has been trusted for a while; dry run still applies. If it can't start (e.g. no start
date) the checklist is created as usual and a technician starts it.

## Trying it on a developer PC

The stand-in agent works against a pretend AD in `artifacts/agent/directory.json`, and GIIM's stand-in cloud lookup
treats an account there as "synced" after a minute.

```powershell
dotnet run --project src/Giim.Api        # Agent:Auth = Key (appsettings.Development.json)
dotnet run --project src/Giim.Workers
dotnet run --project src/Giim.Agent      # STANDIN-AGENT, same dev key
cd src/Giim.Web; npm run dev
```

1. Open a starter checklist (or create one: **Starters and leavers → New starter**). The **Automation** panel shows
   the agent connected and **Run N automated steps**.
2. Run it. In dry-run mode each step reports what it would do, and the tasks go back to you with the plan in their notes.
3. To run for real, start the API with dry run off: `$env:Automation__DryRun='false'; dotnet run --project src/Giim.Api`.
   Then look at `artifacts/agent/directory.json`: the account, mailbox flag and groups; and the welcome email in
   `artifacts/mail`.
4. To see a failure, add a group named `MISSING-something` to a starter profile.

## Turning it on in Azure

1. Run `infra/scripts/New-GiimAppRegistration.ps1` again: it adds the `Giim.Agent` role and the API address the agent
   asks for tokens for.
2. On the agent's server, create its certificate and export the public part (commands at the top of
   `infra/scripts/New-GiimAgentRegistration.ps1`), then run that script with the `.cer`. It prints the agent's settings.
3. Put `agentClientId` in `infra/params/<environment>.bicepparam` and run `./infra/deploy.ps1`. GIIM now accepts that
   agent, and finds new accounts through Graph (the staff directory permission already covers it).
4. Install the agent on its server:

   ```powershell
   dotnet publish src/Giim.Agent -c Release -r win-x64 --self-contained -o .\giim-agent   # on a build machine
   # copy the folder to C:\Program Files\GIIM Agent, fill in appsettings.json, then (as an administrator):
   sc.exe create "GIIM Agent" binPath= "C:\Program Files\GIIM Agent\Giim.Agent.exe" obj= "DOMAIN\gmsa-giim-agent$" start= delayed-auto
   sc.exe failure "GIIM Agent" reset= 86400 actions= restart/60000/restart/60000/restart/300000
   New-EventLog -LogName Application -Source Giim.Agent   # once, so the service account can write to the event log
   ```

   Give the gMSA read access to the certificate's private key (certlm.msc → the certificate → All Tasks → Manage
   Private Keys).
5. **Setup → Automation** shows the agent connected. Leave `automationDryRun = true` and run a few starters as dry runs
   with the AD administrators, then set it to `false` and deploy.

## Settings

GIIM (`Automation:*` and `Agent:*`; in Azure from the `.bicepparam` file):

| Setting | Default | |
|---|---|---|
| `Automation:DryRun` | `true` | Only report what would be done (`automationDryRun` in Azure) |
| `Automation:EnableAt` | `06:00` | When on the start date the account is enabled (`Automation:TimeZone`, default Australia/Sydney) |
| `Automation:CloudSyncCheckEvery`, `CloudSyncTimeout` | 2 minutes, 24 hours | Waiting for Entra Connect |
| `Automation:AgentOfflineAfter` | 5 minutes | When the agent counts as offline (the bell tells administrators if steps are waiting) |
| `Automation:StartFromServiceDesk` | `false` | Start automation when a starter ticket creates the checklist (`automationStartFromServiceDesk` in Azure) |
| `Agent:Auth` | `None` | `Entra` in Azure (set by the deployment when `agentClientId` is given); `Key` on a developer PC |

The agent (`appsettings.json` on its server):

| Setting | |
|---|---|
| `Agent:GiimUrl` | GIIM's address |
| `Agent:Auth`, `TenantId`, `ClientId`, `GiimClientId`, `CertificateThumbprint` | Printed by `New-GiimAgentRegistration.ps1` |
| `Agent:Directory` | `ActiveDirectory` (once built) or `StandIn` |
| `Agent:DryRun` | `true` makes this agent change nothing whatever GIIM asks (a second safety switch) |
| `Accounts:NameFormat`, `UpnSuffix`, `DefaultOu`, `DepartmentOus`, `RemoteRoutingDomain`, `AllowedGroups` | The AD administrators' rules |

## Still to build: the real AD and Exchange steps

`Giim.Agent/Accounts/IDirectory.cs` is the contract; the stand-in implements it. The real implementation will use the
ActiveDirectory module and the Exchange Management Shell (or `System.DirectoryServices` for AD):

| Operation | Command |
|---|---|
| Find by employee ID | `Get-ADUser -Filter "employeeID -eq '<id>'"` |
| Is a sign-in name free | `Get-ADUser -Filter "sAMAccountName -eq '<name>'"` |
| Create (disabled, random password) | `New-ADUser … -Enabled $false -AccountPassword <random SecureString> -EmployeeID … -Path <OU>` |
| Enable remote mailbox | `Enable-RemoteMailbox <upn> -RemoteRoutingAddress <sam>@<tenant>.mail.onmicrosoft.com` |
| Add to group | `Add-ADGroupMember <group> -Members <sam>` |
| Enable | `Enable-ADAccount <sam>` |

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "No on-prem agent is connected" | The service isn't running, can't reach GIIM on 443, or its certificate/thumbprint is wrong (Windows event log, Application, source Giim.Agent) |
| Agent log: GIIM returned 401 | `agentClientId` not deployed, the certificate isn't the one registered, or `Giim.Agent` role not granted (run `New-GiimAgentRegistration.ps1` again) |
| A group step fails "isn't on this agent's list" | Add it to `Accounts:AllowedGroups`, or correct the starter profile's group name |
| "Wait for Entra Connect" fails after 24 hours | Entra Connect isn't syncing the starter OU |
| Every step says "Dry run" | `Automation:DryRun` (GIIM) or `Agent:DryRun` (the agent) is on |
