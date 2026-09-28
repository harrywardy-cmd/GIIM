# Architecture

## Context

| System | Role | How GIIM talks to it |
|---|---|---|
| ServiceDesk Plus Cloud (AU, `servicedeskplus.net.au`, portal `itdesk`) | Where requests are raised; the ticket record | Webhooks in (custom triggers); REST API v3 out (Zoho OAuth via `accounts.zoho.com.au`) |
| On-prem Active Directory | **Identity source of truth** | On-prem agent (outbound only, via Azure Service Bus) using the AD PowerShell module |
| Okta | Imports users from AD (AD agent); app access via groups | Okta Management API; Event Hooks for deactivations |
| Entra ID / M365 | Synced from AD by Entra Connect; licences via group-based licensing | Microsoft Graph |
| Exchange (hybrid) | Mailbox attributes are owned by on-prem AD | `Enable-RemoteMailbox` via the on-prem agent; Exchange Online for shared-mailbox conversion |
| Intune | Source of device facts (serial, primary user, last sync, compliance) | Scheduled full read of Graph managedDevices (`$select`, paged; the endpoint has no delta query) |

## Identity flow

```
GIIM ──► on-prem agent ──► Active Directory ──► Okta (AD agent import) ──► apps via Okta groups
                                     │
                                     └──► Entra Connect ──► Entra ID ──► M365 licence group ──► Exchange Online mailbox
```

Sync is not instant (Entra Connect runs about every 30 minutes). Workflows therefore have explicit
**"waiting for sync"** steps that poll until the account exists, rather than assuming it does.

## Runtime (Azure, Australia East)

```
React UI ──► Giim.Api (App Service, stateless, scales out)
                │
                ├─► Azure SQL (geo-replica to Australia Southeast)
                └─► Service Bus queues ──► Giim.Workers
                                             ├─ SDP connector
                                             ├─ Okta connector
                                             ├─ Graph connector (Entra, Exchange Online, Intune)
                                             └─ on-prem agent (AD + Exchange Management Tools)
Key Vault + Managed Identity · Application Insights · Private endpoints · Okta SSO for staff login
```

## Key design decisions

1. **Onboarding comes from department profiles; offboarding comes from actual assignments.**
   People accumulate kit and access over time, so a template-only leaver list misses things.
   See `ChecklistGenerator` in `Giim.Domain`.
2. **Serial number is the matching key** across Excel, SDP and Intune. Serials are normalised (trimmed, upper-cased) on import.
3. **The UI never calls external APIs live.** Workers sync into Azure SQL (scheduled Intune reads, Okta events, SDP webhooks),
   which keeps the app fast and within API rate limits at 150k+ devices.
4. **Managed app catalogue vs discovered inventory.** Only catalogue apps (with an owner, licence model and Okta group)
   appear in profiles. Raw Intune app inventory is for reporting only.
5. **Hardware lifecycle is enforced in code** (`AssetLifecycle`): e.g. a returned device must be wiped before going back to stock.
6. **Destructive automation needs approval** and everything is written to `AuditEntries`.
7. **On-prem agent connects outbound only.** No inbound firewall rules into the corporate network.
8. **Hybrid Exchange assumed.** Mailboxes are enabled on-prem (`Enable-RemoteMailbox`) before licensing.
   If the environment turns out to be cloud-only, that step is switched off.

## Data model (summary)

```
Department ── RoleProfile ── ProfileItem (Application | Hardware | OktaGroup | LicenceGroup | ManualTask)
Person ─┬─ ServiceCase (Onboarding | Offboarding | Move | HardwareRequest | SoftwareRequest | Rma) ── SDP request ID
        │     └─ ChecklistTask (Manual | Automated, approval, status)
        └─ Assignment ──► Asset (lifecycle status)  or  ──► Application
AuditEntry (append-only)
```

## Security

- The platform can create and disable any user account, so it is treated as a **high-value system**:
  Okta SSO with MFA, role-based access from Okta groups, least-privilege service accounts per integration,
  secrets only in Key Vault, private networking, full audit trail.
- Data is hosted in Australia (Privacy Act); leaver data retention will be agreed with HR and Legal.
