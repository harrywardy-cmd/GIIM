# The staff directory (from Entra ID)

GIIM keeps its own list of staff: who they are, their department, manager and status. Checklists, device requests,
approvals and manager emails all depend on it. In Azure the list is read from **Entra ID** through Microsoft Graph,
every 4 hours, and on request (**People → Sync directory**, administrators). GIIM only reads; it never changes an account.

Accounts synced from on-premises AD by Entra Connect already carry what GIIM needs, as long as those attributes are
filled in in AD.

## What GIIM reads

| GIIM | Entra ID (AD attribute) | Notes |
|---|---|---|
| Employee ID | `employeeId` (`employeeID`) | **Required.** Accounts without one (rooms, shared mailboxes, service accounts) are skipped. GIIM matches people by it, including starters added by hand before their account exists |
| Name | `displayName` | |
| Sign-in name, email | `userPrincipalName`, `mail` | Also used to match managers and approvers |
| Department | `department` | Matched to GIIM's departments by name. New names become new departments |
| Job title, location | `jobTitle`, `officeLocation` (`physicalDeliveryOfficeName`) | |
| Manager | `manager` | **Approvals depend on this**: a manager approves requests for the people who report to them |
| Start date | `employeeHireDate` | Optional. Before this date the person is *Pending* |
| Leave date | `employeeLeaveDateTime` | Optional and off by default; needs an extra permission (below) |
| Track (Full or Light) | an extension attribute, e.g. `extensionAttribute5` | Optional; otherwise set per starter in GIIM |

Only member accounts are read (no guests). To narrow it further, e.g. to one company, set `directoryFilter`.

## Status

| Status | When |
|---|---|
| **Pending** | The start date is in the future. A starter IT added by hand stays Pending while their account is still disabled |
| **Active** | Enabled, started, no leave date |
| **Leaving** | Enabled with a leave date that hasn't passed (only when leave dates are read) |
| **Left** | The account is disabled, or the leave date has passed. Devices can't be assigned to someone who has left |

People are never deleted from GIIM, so their equipment history stays. Someone whose account is deleted from Entra
just stops being updated; the sync reports how many.

---

## For the Entra administrator

1. **Check the data**: in Entra admin centre → Users, add the columns *Employee ID*, *Department* and *Manager* and
   look through a few departments. Fill gaps in AD (or Entra, for cloud-only accounts) before go-live: people without
   an employee ID won't appear in GIIM, and people without a manager have their requests approved by a GIIM
   administrator instead.
2. **Grant the permission** (once per environment, after `deploy.ps1`; needs Privileged Role Administrator or Global
   Administrator). The same script grants the Intune permission:

   ```powershell
   Install-Module Microsoft.Graph.Applications -Scope CurrentUser   # first time only
   az login
   ./infra/scripts/Grant-GraphAccess.ps1 -Environment prod
   ```

   This gives GIIM's two managed identities **User.Read.All** (read-only). Add `-IncludeLeaveDates` to also grant
   **User-LifeCycleInfo.Read.All** if HR records leave dates in Entra.

## For the GIIM administrator

Settings in `infra/params/<environment>.bicepparam`, applied with `./infra/deploy.ps1`:

| Parameter | Default | |
|---|---|---|
| `directoryFilter` | empty | Extra filter on accounts, e.g. `companyName eq 'Contoso'` |
| `directoryTrackAttribute` | empty | Extension attribute holding Full or Light, e.g. `extensionAttribute5` |
| `directoryReadLeaveDates` | `false` | `true` after granting `-IncludeLeaveDates` |

After the first sync, open **People** and check a few people, their departments and managers. Then set up
**Starter profiles** for each department (departments come from the sync, so do this after it).

**Departments** are matched by name, so a department you created with your own code (e.g. `FIN` for Finance) is
kept. If a department is **renamed** in AD, GIIM sees a new department: move its starter profile across, or rename
the department in GIIM to the new name *before* the next sync.

On a developer PC the source is a CSV file (`People:Source = File`, `samples/people.csv`) and syncs only on request.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Sync fails with `403 Authorization_RequestDenied` | `Grant-GraphAccess.ps1` hasn't been run for this environment (or with `-IncludeLeaveDates` when `directoryReadLeaveDates` is on) |
| Someone is missing from GIIM | No employee ID, a guest account, or excluded by `directoryFilter`. The workers log how many accounts were skipped |
| A manager doesn't see their team's requests under Approvals | The person's manager isn't set in AD/Entra, or the manager has no employee ID so isn't in GIIM |
| A starter shows as Left | Their account is disabled and has no future hire date. Set `employeeHireDate`, or add them as a starter in GIIM first (they then stay Pending) |
| Two departments for one team | The department was renamed in AD; see above |
