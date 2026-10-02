# Go-live checklist

The steps to take GIIM from "built and tested" to the service desk using it, in order. Each step links to the runbook
that has the detail. Tick them off here (or copy the list into a ticket).

**Who's needed:** Azure subscription Owner · Entra administrator (Application Administrator, plus Privileged Role
Administrator for one step) · Exchange / Microsoft 365 administrator · ServiceDesk Plus administrator · the GIIM
administrator (IT lead) · two or three technicians and a manager for the pilot.

## 1. A few weeks before

- [ ] Azure subscription for GIIM, and who owns the cost.
- [ ] Entra groups: **GIIM-SQL-Admins** (two people), **GIIM-Administrators**, **GIIM-Technicians**,
  **GIIM-Managers** (or the existing all-managers group), **GIIM-Viewers**. Create them in AD if groups are managed
  there.
- [ ] GIIM's address, e.g. `giim.company.com.au`, and someone who can add its DNS records.
- [ ] A shared mailbox for GIIM to send from, e.g. `giim@company.com.au`, and the IT team address for the daily email.
- [ ] ServiceDesk Plus administrator booked: about 2 hours to set up the integration account, request templates and the
  trigger ([servicedesk-setup.md](servicedesk-setup.md)).
- [ ] Alert email addresses (who hears about errors and failed syncs).
- [ ] **Staff data in AD/Entra**: employee ID, department and manager filled in for every member of staff. GIIM's
  staff list, approvals and manager emails come from these ([staff-directory.md](staff-directory.md)). This is
  usually the longest job on the list, so start it first.

## 2. Test environment

Follow [infra/README.md](../infra/README.md), "First deployment", with `-Environment test`:

- [ ] Steps 1-3: parameters, `deploy.ps1`, database access.
- [ ] Step 4: GitHub environments and variables, `DEPLOY_ENABLED = true`, run **Deploy**; `/health` is green.
- [ ] Step 5: Intune and staff directory read permissions. Run an Intune sync and check device numbers look right.
  **People → Sync directory**: check the number of staff, a few departments and managers.
- [ ] Step 6: Microsoft sign-in and the My Apps tile (test app registration, [entra-setup.md](entra-setup.md)).
- [ ] Email: mailbox permission and `notificationMailbox` ([email-notifications.md](email-notifications.md));
  `itTeamEmails` set.
- [ ] ServiceDesk Plus: connect to a test template or a test ticket category first ([servicedesk-setup.md](servicedesk-setup.md)).

## 3. Try it on test

With real (copied) data, by the pilot group:

- [ ] Each role signs in from My Apps and sees only what it should; someone with no role can't sign in.
- [ ] **Setup:** locations, asset categories, and starter profiles for two or three departments (after the directory
  sync, which creates the departments).
- [ ] **Import register:** import the current asset spreadsheet; check the duplicates and blanks report; link
  spreadsheet owners to people.
- [ ] **Intune reconciliation:** look through each finding list; spot-check ten devices against Intune.
- [ ] **Starter:** raise a starter ticket in SDP → checklist and device request appear in GIIM → manager gets the email
  and approves → hand over from stock → task ticks off → notes appear on the ticket.
- [ ] **Leaver:** leaver ticket → checklist lists their devices → return one, report one lost → both tasks close;
  access tasks need an administrator's approval.
- [ ] **Labels:** print a sheet, scan with a phone and a 2D scanner.
- [ ] **Files:** add a photo from a phone and a PDF to a device; open both from another computer.
- [ ] **Daily email:** Setup → Notifications → **Send now** on the IT digest; check it arrives and reads well.
- [ ] **Restore drill:** restore the test database to an hour ago ([infra/README.md](../infra/README.md), "Restoring
  the database"), then delete the copy.

## 4. Production

- [ ] Repeat steps 1-6 of the runbook with `-Environment prod` (production app registration; GitHub `prod` environment
  needs an approver).
- [ ] **Custom domain** (runbook step 7) and `publicBaseUrl`, **before printing any labels**: QR codes contain the address.
- [ ] Email and ServiceDesk Plus as on test, with the live templates and trigger.
- [ ] Assign the Entra groups to the production app (Users and groups). Staff without a role don't see the tile.
- [ ] Setup, register import and owner linking again, this time for real (or restore from a test run everyone is happy with).

## 5. Go-live day

- [ ] Freeze the old spreadsheet (read-only), so there's one register.
- [ ] Announce to technicians (send [guide-technicians.md](guide-technicians.md)) and managers
  ([guide-managers.md](guide-managers.md)).
- [ ] Turn on the SDP trigger for starters and leavers.
- [ ] Watch Application Insights → **Failures**, and Setup → ServiceDesk Plus for tickets GIIM couldn't use.

## 6. First weeks

- [ ] Work through the Intune reconciliation lists until they're short.
- [ ] Finish the starter profiles for every department.
- [ ] Check the first leavers' returns and reminders went as expected.
- [ ] Review who holds the Administrator role; remove anyone who only needed it for set-up.

## 7. Starter automation (when the AD administrators are ready)

- [ ] Agree the decisions in [onprem-agent.md](onprem-agent.md) (username format, OUs, groups, service account, server).
- [ ] Set up the agent on test, run a few starters as **dry runs** with the AD administrators, and check the plans.
- [ ] Turn dry run off on test (`automationDryRun = false`), automate a test starter end to end, then do the same on prod.

## If it has to be rolled back

GIIM doesn't change anything outside itself except notes on SDP tickets and emails it sends, so stopping it is safe:
turn off the SDP trigger, stop the two web apps in Azure, and unfreeze the spreadsheet. Nothing in AD, Entra, Exchange
or Intune needs undoing (GIIM only reads Intune). The database is kept, so a later restart continues where it left
off.
