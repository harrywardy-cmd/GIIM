# Email notifications

GIIM emails people about device requests, new starters and leavers, and sends the IT team a daily digest.
Administrators can see every email and whether it was sent under **Setup → Notifications**.

## Device requests


| When | Who gets it |
|---|---|
| A request is raised | The approver (the recipient's manager, or whoever was chosen) |
| The approver asks for more information | The person who raised the request |
| The information is provided | The approver |
| The request is approved or rejected | The person who raised it |
| The device is handed over | The person who raised it |

Nobody is emailed about their own action. Every email links straight to the request in GIIM.

## Starters, leavers and reminders

| When | Who gets it |
|---|---|
| A starter checklist is created | Their manager: start date, the devices being arranged (and which requests need their approval), apps and access |
| A leaver checklist is created | Their manager: last day and the equipment to collect |
| A device request has waited 2 days for approval | The approver, then every 2 days, at most 3 reminders. If the approver asked a question, the 2 days restart when it is answered |
| A leaver's last day has passed and equipment is still out | Their manager, the next day, then weekly, at most 3 reminders. Equipment reported lost or stolen isn't included |
| A device goes for repair, and when the repair is done (repaired, or can't be) | The person it's assigned to: the fault, where it went, the result |
| A starter's account is enabled on their start date (starter automation) | Their manager |
| Daily at 07:30 | The IT team: overdue checklists, unreturned equipment, starters and leavers in the next 3 days, device requests held up. **Only sent when something needs attention** |
| Mondays at 07:30 | The IT team: warranties ending in the next 30 days |

Each email is sent once: checklists and requests remember what was sent, and each scheduled job remembers the day it
last ran, so restarts and frequent checks never send anything twice. **Setup → Notifications → Send now** sends a
digest straight away (useful to check it); that counts as that day's run.

| Setting (`Reminders:`) | Default |
|---|---|
| `ItTeamAddresses` | Who gets the digests, separated by `;`. In Azure: `itTeamEmails` in the `.bicepparam` file |
| `Enabled`, `ManagerEmails`, `RepairEmails` | `true`, `true`, `true` |
| `DigestTime`, `WarrantyDay`, `TimeZone` | `07:30`, `Monday`, `Australia/Sydney` |
| `DueSoonDays`, `WarrantyDays` | `3`, `30` |
| `ApprovalReminderAfter`, `MaxApprovalReminders` | 2 days, `3` |
| `ReturnReminderEvery`, `MaxReturnReminders` | 7 days, `3` |

## How it works

When something happens, the email is saved in GIIM's database in the same step as the change itself (an
"outbox"). The background workers send waiting emails every 30 seconds. If sending fails, they retry after 1, 2, 4,
8... minutes, up to 8 attempts. An email that still hasn't gone after 3 days (for example because email was switched
off) is marked expired rather than sent late. Each request page lists its emails and whether they were sent.

| `Email:Mode` | What happens |
|---|---|
| `None` | Emails wait in the outbox. The default until a mailbox is set up. |
| `File` | Each email is written as an `.eml` file to `artifacts/mail` (open it in Outlook). Used on developer PCs. |
| `Graph` | Sent through Microsoft 365 from `Email:FromMailbox`, as the workers' managed identity. Used in Azure. |

In Azure, setting `notificationMailbox` in `infra/params/<environment>.bicepparam` and running `deploy.ps1` switches
the workers to `Graph` mode with that mailbox.

---

## For the Microsoft 365 / Exchange administrator

### 1. Create the mailbox

Create a **shared mailbox** (no licence needed), for example `giim@company.com.au`, display name **GIIM**. Replies
aren't expected. Consider an auto-reply saying so, or deliver replies to the IT team.

### 2. Let GIIM send from that mailbox, and only that mailbox

Microsoft Graph's `Mail.Send` application permission on its own would let an app send as **anyone** in the
organisation. Limit it to GIIM's mailbox with **RBAC for Applications in Exchange Online**, which grants the right
for one mailbox and nothing else:

```powershell
Connect-ExchangeOnline

# The workers' managed identity: its client ID and object ID are on the identity in the Azure portal
# (Managed Identities > id-giim-prod-workers > Overview), or in the deploy.ps1 outputs.
$clientId = "<workers identity client ID>"
$objectId = "<workers identity object (principal) ID>"

New-ServicePrincipal -AppId $clientId -ObjectId $objectId -DisplayName "GIIM workers (prod)"
New-ManagementScope -Name "GIIM mailbox" -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'giim@company.com.au'"
New-ManagementRoleAssignment -App $clientId -Role "Application Mail.Send" -CustomResourceScope "GIIM mailbox"

# Check: should say it is allowed for the GIIM mailbox...
Test-ServicePrincipalAuthorization -Identity $clientId -Resource giim@company.com.au
# ...and not for anyone else's.
Test-ServicePrincipalAuthorization -Identity $clientId -Resource someone.else@company.com.au
```

Do **not** also grant `Mail.Send` to the identity in Entra ID: that tenant-wide permission would override the
mailbox limit. If your tenant can't use RBAC for Applications, the older alternative is to grant `Mail.Send` in
Entra and restrict it with `New-ApplicationAccessPolicy` to a mail-enabled security group containing only the GIIM
mailbox. Check it with `Test-ApplicationAccessPolicy` in the same way.

### 3. Turn it on

Set `notificationMailbox = 'giim@company.com.au'` in the environment's `.bicepparam` file and run
`./infra/deploy.ps1 -Environment prod`. Raise a test request to check it arrives. If it doesn't, the request page
shows the email as retrying, and the workers' log in Application Insights has the reason (search for
`Email RequestApprovalNeeded`).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Emails stay "Queued" | `Email:Mode` is `None` (no mailbox set), or the workers aren't running |
| Retrying with `403` / `ErrorAccessDenied` | The Exchange role assignment is missing or scoped to a different mailbox |
| Retrying with `404` / `ErrorInvalidUser` | `notificationMailbox` doesn't match the mailbox's address |
| Arrives in junk | Ask the Exchange admin to check SPF/DKIM for the sending domain (usually fine for a mailbox in your own tenant) |
