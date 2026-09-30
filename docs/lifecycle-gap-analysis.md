# Asset lifecycle brief: gap analysis

Compares the **IT Asset Tracking & Lifecycle Management** brief ([requirements/asset-lifecycle-brief.md](requirements/asset-lifecycle-brief.md))
and its UI mockups ([design/ui-mockups.png](design/ui-mockups.png)) with what GIIM has already built or planned.

Rule applied: **only features that are new are added to the roadmap.** Where the brief describes something GIIM already
has or plans, the existing item stays and any extra detail from the brief is folded into it.

## 1. Already built or planned (not added again)

| Brief section | GIIM today | Extra detail taken from the brief |
|---|---|---|
| §10 Asset record (tag, serial, make, model, type, purchase, warranty, location, notes) | ✅ Built (asset register, Excel import) | Photos, attachments and QR code (see new features) |
| §13 Accessories without serials | ✅ Built (stock items and levels) | Linking accessories to a parent asset (see new features) |
| §25 Audit log | ✅ Built (`AuditEntries`: imports, stock) | Previous/new value on **every** change, ticket on each entry |
| §15 Ticket linking | ◐ Planned: every case and assignment carries an SDP request ID | Ticket on every lifecycle action; search by ticket |
| §28 Employee offboarding | ◐ Planned: Phase 4, checklist built from actual assignments | "Clearly show anything not returned" becomes the return workflow (below) |
| §29 Employee onboarding | ◐ Planned: Phases 2 and 3 | Device request and approval as the first step (see new features) |
| §27 Employee profile: name, email, department, manager, location | ◐ Planned: people import from AD (Phase 1) | Profile **page** (see new features) |
| §3 Roles and access | ✅ Microsoft Entra sign-in (My Apps) with roles from Entra app roles | Four named roles: Administrator, Technician, Manager/Approver, Viewer |
| §19 Data-wiping confirmation | ◐ Built in the lifecycle (`Wiped` must come before reuse) | Also required at retirement |
| §17 Repairs (status only) | ◐ Built: `InRepair` status | Full repair record (see new features) |
| §21 Warranty expirations | ◐ Planned: Phase 5 warranty lookups from vendor APIs | Expiry list and alerts from the data already stored (see new features) |
| §24 Notifications (email) | ◐ Planned: welcome and return-reminder emails via Graph | In-app notifications and the full event list (see new features) |
| Intune, Entra ID, AD, Exchange, SDP integration | ✅/◐ Built or planned | Not in the brief; unchanged |

## 2. New features added from the brief

| # | Feature | Brief | Mockup |
|---|---|---|---|
| N1 | **Extended lifecycle statuses**: Received, Ready to deploy (Available), Stolen, Retired | §2, §10 | 1, 4 |
| N2 | **Asset timeline**: append-only history per asset, with technician and ticket on every event | §14, §25 | 4 |
| N3 | **Assign / return workflow**: employee, technician, ticket, location, accessories, condition, missing items | §12, §16 | 4 |
| N4 | **Accessory bundles**: accessories linked to a parent asset and checked off on return | §13, §16 | 4 |
| N5 | **Repair records**: fault, diagnosis, repair, vendor, cost, dates sent and returned | §17 | – |
| N6 | **Lost / stolen records**: date, reported by, circumstances | §18 | – |
| N7 | **Retirement and disposal records**: reason, wipe confirmation, method, company, certificate number | §19, §20 | – |
| N8 | **Locations** as a managed list (currently free text) | §32 | 1 (nav) |
| N9 | **Asset details page** | §11 | 4 |
| N10 | **Global search**: tag, serial, employee, **ticket**, request ID, model, technician | §22, §15 | 1 |
| N11 | **QR codes**: printable label, scan-to-open mobile asset page with Assign / Return / Repair | §23 | 4 |
| N12 | **Photos and attachments** on assets, requests, returns and repairs | §9, §10, §16 | 3 |
| N13 | **Device requests**: REQ numbers, requester, recipient, device, specs, priority, reason; statuses | §4, §5, §31 | 2 |
| N14 | **Approval workflow**: approve, reject (reason required), request more info; budget / cost centre | §6, §7 | 3 |
| N15 | **Purchasing**: supplier, PO, cost, order date, expected delivery, tracking | §8 | 3, 4 |
| N16 | **Receiving**: turn a request into one or more asset records | §9 | 3 |
| N17 | **Dashboard**: summary cards, status chart, requests this month, warranty expiring, recent activity | §21 | 1 |
| N18 | **Reports and analytics** with CSV / Excel export (inventory, requests, repairs, warranty, technician activity) | §26 | 7 |
| N19 | **Employee profile page**: assigned assets, requests, history | §27 | 6 |
| N20 | **Tickets view**: everything done under a ticket number | §15, §32 | 1 (nav) |
| N21 | **Notifications**: in-app bell plus email for requests, approvals, receipt, returns, repairs, warranty, overdue returns | §24 | 1 |
| N22 | **UI refresh** to the mockup style: dark sidebar, header search, summary cards | all | all |

## 3. Changes to the existing design

**Lifecycle statuses (N1).** The brief and GIIM mostly agree. Proposed combined list:

| Status | Source | Note |
|---|---|---|
| Received | Brief | Delivered, not yet set up |
| **Ready to deploy** | Brief (= GIIM `InStock`, shown as "Available") | Renamed |
| Assigned | Both | |
| Return requested | GIIM | Kept: this is how offboarding shows kit that hasn't come back yet (§28) |
| Returned | Both | |
| Wiped | GIIM | Kept as a security control: a returned device must be wiped before it is redeployed |
| In repair | Both | |
| Lost / **Stolen** | Brief adds Stolen | Separate, because stolen devices usually need a police report and an Intune wipe |
| **Retired** | Brief | Out of service; wipe confirmation required |
| Disposed | Both | Final |

**Roles (§3)** become four Entra app roles (Administrator, Technician, Manager/Approver and Viewer), assigned to groups.

**SSO moves earlier.** The brief's core promise is *who* did each action. Until staff sign in with their Microsoft accounts, every action is
recorded as `local-dev`, so SSO and roles now come before the lifecycle work.

**Storage.** Photos and attachments (N12) need Azure Blob Storage, which is added to the infrastructure plan.

## 4. Decisions (29 Sep 2026)

1. **Device requests and approvals live in GIIM** (option B, as in mockups 2 and 3). ServiceDesk Plus was the
   alternative (it already has approval workflows); it was not chosen. SDP tickets are linked as references.
   Consequences:
   - **Approval notifications (N21) ship with approvals (N14), not later.** Managers need an email with a link to the
     request, or requests sit unseen in a system they don't use daily.
   - Managers sign in (from My Apps) as **Manager/Approver** and see their team's requests.
   - **To avoid two ways of requesting kit**, the SDP hardware request templates should be retired or pointed at GIIM.
     This needs agreeing with the SDP administrator before go-live.
   - New-starter tickets arriving from SDP (Phase 2 webhook) create a GIIM device request automatically, pending approval.
2. **Product name: GIIM.** The UI uses the mockup layout and styling with the GIIM name.
3. **No label printer.** QR codes are a downloadable PNG per asset (mockup 4, "Download QR") plus a printable A4 sheet
   of labels for a batch of assets, for standard A4 sticker sheets.
