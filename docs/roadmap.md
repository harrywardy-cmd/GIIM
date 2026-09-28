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
8. People import from AD (read-only) and department profile editor
9. Manual checklists linked to an SDP request ID
10. Okta SSO for staff login; Azure environment (Bicep) in Australia East

**Exit criteria:** the team uses GIIM as the asset register, and the reconciliation report has been worked through.

## Phase 2: ServiceDesk Plus workflow

1. SDP request templates with structured fields (department, role, start/leave date, manager, location)
2. SDP custom trigger → webhook → GIIM creates the case and checklist automatically
3. GIIM writes progress notes back to the ticket and resolves it when the checklist is done
4. Hardware request, replacement and RMA flows (asset check-out/check-in by barcode scan)

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
4. Hardware return tracking with manager reminders; Intune wipe/retire on return
5. Disable AD account and move to the Leavers OU

## Phase 5: Maturity

- Mover (department change) flow: add the new access, remove the old
- Warranty lookups (Dell / Lenovo / HP APIs)
- Licence utilisation and reclaim reporting
- Dashboards: starters and leavers this week, kit outstanding, stock levels
- Light track rolled out to larger frontline groups
