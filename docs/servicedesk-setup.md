# Connecting ServiceDesk Plus

With ServiceDesk Plus Cloud connected, GIIM:

- **creates the starter or leaver checklist** when a New Starter or Leaver ticket is raised, and (for starters) raises
  the device requests straight away, so the manager's approval email goes out on day one;
- **adds notes to the ticket** as things happen: checklist created, device request raised, approved or rejected,
  device handed over, checklist complete or cancelled. Notes are for technicians only (not shown to the requester);
- optionally **resolves the ticket** when its checklist is complete (`ResolveWhenComplete`; agree this with the
  service desk team first).

If GIIM can't create a checklist from a ticket (say the department isn't in GIIM), it notes that on the ticket and
lists it under **Setup → ServiceDesk Plus** as *Needs attention*. Fix the cause, then press **Retry**. GIIM never
guesses: an unknown department, manager or leaver stops it rather than creating the wrong thing.

## How it works

```
New Starter ticket ──► SDP custom trigger ──► webhook: {"requestId": "<id>"} + shared secret ──► GIIM inbox
                                                                                               │
GIIM workers ──► read the ticket from the SDP API ──► create checklist ──► raise device requests
      │
      └──► notes back to the ticket (SDP API), retried until they get through
```

- The webhook carries **only the ticket's ID**. GIIM reads the ticket itself through the API, so a forged call can't
  create anything; it also needs the shared secret, is rate-limited and accepts only small requests.
- Duplicate webhooks are ignored, and a ticket that already has a checklist isn't processed twice.
- Checklists created by hand in GIIM with a ticket number get notes too: GIIM looks the ticket up by its number.

---

## For the ServiceDesk Plus administrator

Allow about an hour. You need SDP administrator rights and access to the Zoho API console.

### 1. An integration account and API client

1. Create (or choose) an SDP technician account for GIIM, e.g. **GIIM Integration**, with a role that can **view
   requests and add notes** (and close requests, only if tickets are to be resolved automatically). The API client acts
   as this account, and notes show as added by it.
2. Signed in as that account, open the **Zoho API console** for the AU data centre (`https://api-console.zoho.com.au`)
   and create a **Self Client**. Note the **client ID** and **client secret**.
3. In the Self Client, **Generate Code** with scope `SDPOnDemand.requests.ALL`, time duration 10 minutes, and a
   description such as "GIIM". Copy the code, then within 10 minutes exchange it for a **refresh token**:

   ```powershell
   $r = Invoke-RestMethod -Method Post -Uri 'https://accounts.zoho.com.au/oauth/v2/token' -Body @{
       grant_type = 'authorization_code'; client_id = '<client id>'; client_secret = '<client secret>'; code = '<code>' }
   $r.refresh_token
   ```

4. Give the GIIM administrator the **client ID** (not secret), and the **client secret** and **refresh token**
   through a password manager (never email or Teams). The refresh token doesn't expire unless revoked, so treat it
   like a password.

### 2. The request templates and their fields

GIIM needs these details from the **New Starter** and **Leaver** templates (the names can differ; tell the GIIM
administrator what yours are called):

| Starter detail | Notes |
|---|---|
| Employee ID | From HR; how GIIM matches the person once their account exists |
| Name | Full name |
| Department | Department code or name, exactly as in GIIM (e.g. `FIN` or `Finance`) |
| Job title | Optional; picks a job-title starter profile if there is one |
| Manager's email | Optional; the manager approves the starter's devices |
| Start date | A date field |
| Track | Optional: `Full` or `Light` |

| Leaver detail | Notes |
|---|---|
| Employee ID **or** email | Either is enough |
| Last day | A date field |

Send the GIIM administrator each field's **API name** (for custom fields, like `udf_sline_301` or `udf_date_306`;
visible in the field's settings). They go in `config/servicedesk.json`.

### 3. The trigger

Create a **custom trigger** for requests:

- **When:** a request is created;
- **Condition:** the template is New Starter or Leaver;
- **Action:** a webhook
  - URL: the **Webhook URL** printed by `infra/deploy.ps1` (`https://<giim-address>/integrations/servicedesk/webhook`)
  - Method: **POST**, content type **application/json**
  - Header: `X-GIIM-Webhook-Secret` = the webhook secret from the GIIM administrator
  - Body: `{"requestId": "<request ID>"}`, inserting the request's ID from the list of available placeholders

If GIIM only accepts office or VPN addresses (`allowedIpRanges`), ServiceDesk Plus's outbound addresses must be added
too, or the webhook can't reach it.

---

## For the GIIM administrator

1. Put the template names and field API names into **`config/servicedesk.json`** and commit them (not secret).
2. In `infra/params/prod.bicepparam` set `serviceDeskMode = 'Api'` and `serviceDeskClientId`.
3. Run `./infra/deploy.ps1 -Environment prod -SetServiceDeskSecrets`. Paste the client secret and refresh token when
   asked, and leave the webhook secret blank to have one generated; it is shown once, to give to the SDP administrator.
4. Raise a test **New Starter** ticket and watch **Setup → ServiceDesk Plus**: within a minute the ticket should show
   *Checklist created*, and a note should appear on it.

| Setting | Meaning |
|---|---|
| `ServiceDesk:Mode` | `None` (off), `File` (stand-in for development: tickets from `samples/servicedesk-requests.json`, notes to `artifacts/servicedesk/notes.log`), `Api` |
| `ServiceDesk:SiteUrl`, `Portal` | `https://servicedeskplus.net.au/` and `itdesk` |
| `ServiceDesk:ClientId`, `ClientSecret`, `RefreshToken` | The Zoho Self Client (secrets from Key Vault) |
| `ServiceDesk:WebhookSecret` | Shared secret for the webhook (Key Vault). Empty turns the webhook off |
| `ServiceDesk:Starter`, `Leaver` | Template names and field mapping (`config/servicedesk.json`) |
| `ServiceDesk:AutoRaiseDeviceRequests` | Raise a starter's device requests automatically (default yes) |
| `ServiceDesk:ResolveWhenComplete` | Resolve the ticket when its checklist completes (default no) |

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Nothing appears under Setup → ServiceDesk Plus | The trigger didn't fire, the webhook URL is wrong, or GIIM's `allowedIpRanges` blocks ServiceDesk Plus |
| The trigger's log shows 401 | The `X-GIIM-Webhook-Secret` header doesn't match |
| Ticket shows *Ignored: template isn't set up* | The template name isn't listed in `config/servicedesk.json` |
| *Needs attention: the ticket has no … (field udf_…)* | The field is empty on the ticket, or the API name in `config/servicedesk.json` is wrong |
| *Failed: Zoho sign-in failed* | The refresh token was revoked or the client secret changed; generate new ones and run `deploy.ps1 -SetServiceDeskSecrets` |
| Notes *Retrying* with 403 | The integration account's role can't add notes (or close requests) |
