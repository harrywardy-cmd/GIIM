# Roadmap

Each phase ends with something usable, and nothing destructive is automated until the data underneath it has been proven correct.

## Phase 0: Scaffold ✅

- Solution structure, domain model, database schema (initial migration)
- Asset lifecycle and checklist-generation rules, with unit tests
- Connector contracts for SDP and Intune
- React UI shell with an Assets page
- Fake test data: 1,500 people, 20 departments, ~4,800 devices, messy Excel register, SDP export, Intune export

## Phase 1: Register and reconciliation (read-only, no risk) (current)

Goal: one trustworthy list of who has what.

1. ✅ Local database and dev environment running (Docker SQL Server)
2. ✅ **Excel importer** with a column-mapping step, serial normalisation, and duplicate/blank detection
3. ✅ **Asset categories and stock levels**: serialised assets tracked individually; items without serials tracked as stock
4. ✅ **Intune device sync**: scheduled full read of Graph managedDevices, read-only; removed devices detected
5. ✅ **Reconciliation report**: not in register / missing from Intune / stale 90+ days / different user / still in use.
   Verified against `samples/expected-reconciliation.json`
6. **Connect to the real Intune tenant** (needs the Entra app permission, see `docs/intune-app-registration.md`)
7. **SDP asset import** (API v3, read-only)
8. ◐ **People sync** ✅ (directory source interface; sample CSV today) and **linking spreadsheet owners** ✅ (name, Intune user, department; ambiguous cases resolved by a technician). Still to do: the Active Directory source and the department profile editor
9. ◐ **Microsoft sign-in (Entra ID, My Apps tile) with four roles** ✅ built and tested with the development sign-in
   (see `docs/entra-setup.md`); creating the app registration is waiting on the Entra admin. Replaced the earlier Okta
   plan (30 Sep 2026: GIIM uses Microsoft Entra ID only). Moved earlier because every
   lifecycle action in Phase 1B must record *who* did it.
10. ◐ **Azure environment** ✅ built: Bicep templates for test and prod in Australia East (App Service, Azure SQL,
    Key Vault, Blob Storage, private networking, monitoring and alerts), GitHub Actions CI and gated deployments,
    managed identities throughout, and a runbook (`infra/README.md`). Deploying it is waiting on an Azure subscription.
    Photos and attachments (the storage is ready) follow as their own item

**Exit criteria:** the team uses GIIM as the asset register, and the reconciliation report has been worked through.

> Phases 1B, 1C and the new items in Phase 2 come from the asset lifecycle brief. See
> [lifecycle-gap-analysis.md](lifecycle-gap-analysis.md) for what was new versus already planned (N-numbers below).

## Phase 1B: Asset lifecycle (new, from the brief)

Goal: every asset has a complete, append-only history, and every action records the technician and ticket.

1. ✅ **Extended lifecycle statuses** (N1): Received, Ready to deploy, Stolen, Retired
2. ✅ **Asset timeline** (N2): append-only events with technician, ticket, previous and new values
3. ✅ **Locations** as a managed list (N8): Locations page, pickers everywhere, Move action, asset filters (status/category/location), import creates missing locations; stock ledger keeps original names
4. ◐ **Asset details page** (N9) and ✅ **UI refresh** to the mockup style (N22): sidebar, top bar with global search, cards, pill badges
5. ✅ **Assign / return workflow** (N3) with **accessory bundles** (N4): tracked accessories, stock items, condition, missing items; return requests
6. ✅ **Repair records** (N5), ✅ **lost / stolen** (N6), ✅ **retirement and disposal** with data sanitisation and certificate (N7)
7. ✅ **Global search** (N10): assets, people, tickets and technicians from the top bar; exact serial/tag/QR/ticket opens directly. ✅ **Tickets view** (N20) and technician activity. Also: **Add asset** by hand with scanner-friendly serial entry and duplicate check, exact serial/tag lookup (Enter or scan opens the device), serial search ignoring spaces/dashes, owners history per asset
8. ✅ **QR codes** (N11): QR on each asset page, PNG download, printable A4 label sheets (21 or 65 per sheet, start-at for part-used sheets); scanning opens the asset (phone camera or 2D scanner); links to assets
   **Photos and attachments** (N12) still to do

## Phase 1C: Visibility (new, from the brief)

1. ✅ **Dashboard** (N17): summary cards, status chart, needs-attention list, warranty expiring, recent activity (trend arrows need history snapshots; later)
2. ✅ **Employee profile page** (N19): details, manager, assigned assets, assignment history and device requests
3. ✅ **Reports** (N18) with CSV / Excel export: asset inventory (status/category/location filters), warranty
   (expiring within N days, expired, not recorded), repairs (cost, turnaround, warranty claims, repeat assets),
   technician activity, leavers still holding kit, stock levels. Screen shows 1,000 rows; exports hold every row and
   are protected against spreadsheet formula injection (request reports are added in Phase 2)

## Phase 2: Requests and the ServiceDesk Plus workflow

Existing items:

1. SDP request templates with structured fields (department, role, start/leave date, manager, location)
2. SDP custom trigger → webhook → GIIM creates the case and checklist automatically
3. GIIM writes progress notes back to the ticket and resolves it when the checklist is done
4. Manual checklists linked to an SDP request ID

New from the brief (replaces the earlier "hardware request, replacement and RMA flows" item):

5. ✅ **Device requests** (N13) with REQ numbers and statuses; request and asset kept separate but linked both ways
6. ✅ **Approvals in GIIM** (N14): approve, reject with reason, request more info; budget / cost centre. The recipient's
   manager approves (or an administrator); nobody approves their own request; a manager raising a request for their
   own team member approves it by doing so. Approvals menu with a count of requests waiting for you.
   ✅ Ships with **approval notifications** (email with a link) through a reliable outbox; see `docs/email-notifications.md`
7. SDP new-starter tickets create a GIIM device request automatically; SDP hardware request templates retired or
   pointed at GIIM (agree with the SDP administrator)
8. ✅ **Purchasing** (N15) and **receiving** (N16): approved request → PO → delivery → asset record created → handed
   over with accessories; or handed over from stock
9. **Other notifications** (N21): in-app and email for receipt, returns, repairs, warranty and overdue returns
10. ✅ Request report added to Reports (approval rate, time to decide, time to hand over), and a pending-approval tile on the dashboard

## Phase 3: Onboarding automation (low risk first)

1. On-prem agent (Windows service, outbound via Service Bus)
2. Create AD user in the correct OU → wait for Entra Connect sync
3. `Enable-RemoteMailbox` (hybrid) → M365 licence group
4. Security group membership from the profile (AD groups via the agent; cloud-only groups via Graph) → app access
5. Activate on start date; welcome email to manager
6. Dry-run mode and full audit for every step

## Phase 4: Offboarding automation (approval-gated)

1. Leaver case from SDP ticket **or** an account disabled in AD / Entra
2. Revoke sessions → shared mailbox + manager access → out-of-office → remove licence
3. Remove app access from actual assignments
4. Hardware return tracking with manager reminders, using the Phase 1B return workflow; Intune wipe/retire on return
5. Disable AD account and move to the Leavers OU

## Phase 5: Maturity

- Mover (department change) flow: add the new access, remove the old
- Warranty lookups (Dell / Lenovo / HP APIs)
- Licence utilisation and reclaim reporting
- Dashboard additions beyond Phase 1C: starters and leavers this week, kit outstanding, stock levels
- Light track rolled out to larger frontline groups
