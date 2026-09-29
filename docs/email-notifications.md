# Email notifications

GIIM emails people about device requests:

| When | Who gets it |
|---|---|
| A request is raised | The approver (the recipient's manager, or whoever was chosen) |
| The approver asks for more information | The person who raised the request |
| The information is provided | The approver |
| The request is approved or rejected | The person who raised it |
| The device is handed over | The person who raised it |

Nobody is emailed about their own action. Every email links straight to the request in GIIM.

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
