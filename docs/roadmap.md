# Roadmap

Each phase ends with something usable, and nothing destructive is automated until the data underneath it has been proven correct.

## Phase 0: Scaffold ✅

- Solution structure, domain model, database schema (initial migration)
- Asset lifecycle and checklist-generation rules, with unit tests
- Connector contracts for SDP, Intune and Okta
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
9. **Okta SSO with four roles**: Administrator, Technician, Manager/Approver, Viewer. Moved earlier because every
   lifecycle action in Phase 1B must record *who* did it.
10. Azure environment (Bicep) in Australia East, including Blob Storage for photos and attachments

**Exit criteria:** the team uses GIIM as the asset register, and the reconciliation report has been worked through.

> Phases 1B, 1C and the new items in Phase 2 come from the asset lifecycle brief. See
> [lifecycle-gap-analysis.md](lifecycle-gap-analysis.md) for what was new versus already planned (N-numbers below).

## Phase 1B: Asset lifecycle (new, from the brief)

Goal: every asset has a complete, append-only history, and every action records the technician and ticket.

1. ✅ **Extended lifecycle statuses** (N1): Received, Ready to deploy, Stolen, Retired
2. ✅ **Asset timeline** (N2): append-only events with technician, ticket, previous and new values
3. **Locations** as a managed list (N8)
4. ◐ **Asset details page** (N9, first version with timeline and actions) and **UI refresh** to the mockup style (N22)
5. ✅ **Assign / return workflow** (N3) with **accessory bundles** (N4): tracked accessories, stock items, condition, missing items; return requests
6. **Repair records** (N5), ✅ **lost / stolen** (N6), **retirement and disposal** with wipe confirmation and certificate (N7)
7. ◐ **Global search** including ticket number (N10) and a **tickets view** (N20). Done so far: **Add asset** by hand with scanner-friendly serial entry and duplicate check, exact serial/tag lookup (Enter or scan opens the device), serial search ignoring spaces/dashes, owners history per asset
8. **QR codes** (N11): PNG download per asset and a printable A4 label sheet (no label printer); mobile scan-to-open
   asset page. **Photos and attachments** (N12)

## Phase 1C: Visibility (new, from the brief)

1. **Dashboard** (N17): summary cards, status chart, warranty expiring, recent activity
2. ◐ **Employee profile page** (N19): details, manager, assigned assets and assignment history done; requests arrive with Phase 2
3. **Reports** (N18) with CSV / Excel export: inventory, repairs, warranty, technician activity
   (request reports are added in Phase 2)

## Phase 2: Requests and the ServiceDesk Plus workflow

Existing items:

1. SDP request templates with structured fields (department, role, start/leave date, manager, location)
2. SDP custom trigger → webhook → GIIM creates the case and checklist automatically
3. GIIM writes progress notes back to the ticket and resolves it when the checklist is done
4. Manual checklists linked to an SDP request ID

New from the brief (replaces the earlier "hardware request, replacement and RMA flows" item):

5. **Device requests** (N13) with REQ numbers and statuses; request and asset kept separate
6. **Approvals in GIIM** (N14): approve, reject with reason, request more info; budget / cost centre.
   Ships with **approval notifications** (email with a link) so managers see requests promptly
7. SDP new-starter tickets create a GIIM device request automatically; SDP hardware request templates retired or
   pointed at GIIM (agree with the SDP administrator)
8. **Purchasing** (N15) and **receiving** (N16): approved request → PO → delivery → asset records created
9. **Other notifications** (N21): in-app and email for receipt, returns, repairs, warranty and overdue returns
10. Request reports added to the Phase 1C reports

## Phase 3: Onboarding automation (low risk first)

1. On-prem agent (Windows service, outbound via Service Bus)
2. Create AD user in the correct OU → wait for Okta and Entra sync
3. `Enable-RemoteMailbox` (hybrid) → M365 licence group
4. Okta group assignment from the profile → app access
5. Activate on start date; welcome email to manager
6. Dry-run mode and full audit for every step

## Phase 4: Offboarding automation (approval-gated)

1. Leaver case from SDP ticket **or** Okta deactivation event
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
