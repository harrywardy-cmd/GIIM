<!--
Source requirements: "IT Asset Tracking & Lifecycle Management System" brief, supplied by Harry Ward on 29 Sep 2026,
together with the UI mockups in docs/design/ui-mockups.png. Kept verbatim; how it maps onto GIIM (what was already
built or planned, and what is new) is in docs/lifecycle-gap-analysis.md.
-->

# IT Asset Tracking & Lifecycle Management System

## 1. Overview

The IT Asset Tracking & Lifecycle Management System is a web application designed to manage the complete lifecycle of IT equipment within an organisation.

The system will allow IT staff to:

- Track physical IT assets.
- Create and manage device requests.
- Assign devices to employees.
- Record serial numbers and asset tags.
- Track which technician performed an action.
- Link actions to support ticket numbers.
- Manage approvals.
- Track purchasing and receiving.
- Track repairs and returns.
- Maintain a complete history of each asset.
- Monitor available and assigned inventory.
- Generate reports and operational insights.

The core principle of the application is:

> **Every asset should have a traceable history from the moment it is requested until the moment it is retired or disposed of.**

---

# 2. Asset Lifecycle

An asset can move through the following lifecycle:

```text
Device Requested
       ↓
Pending Approval
       ↓
Approved / Rejected
       ↓
Ordered
       ↓
Received
       ↓
Asset Created
       ↓
Ready to Deploy
       ↓
Assigned
       ↓
In Use
       ↓
Returned
       ↓
 ┌─────┴─────┐
 ↓           ↓
Ready      Repair
 ↓           ↓
Assigned   Repaired
              ↓
        Ready to Deploy
              ↓
          Assigned
              ↓
           Retired
              ↓
          Disposed
```

Not every asset will follow every stage.

For example, an asset manually added to existing inventory may begin directly at **Received** or **Ready to Deploy**.

---

# 3. Users and Roles

The application will support different levels of access.

## Administrator

Administrators can:

- Manage users.
- Manage roles.
- Create and edit assets.
- Create and approve requests.
- Assign and return assets.
- Manage locations and categories.
- View reports.
- View audit logs.
- Configure application settings.

## Technician

Technicians can:

- Create asset records.
- Process requests.
- Assign assets.
- Return assets.
- Record repairs.
- Add notes.
- Link ticket numbers.
- Update asset information.
- View asset history.

## Manager / Approver

Managers can:

- View requests requiring approval.
- Approve requests.
- Reject requests.
- Add approval comments.
- View their team's requested and assigned assets.

## Viewer

Viewers have read-only access to relevant information.

---

# 4. Device Request

The lifecycle begins when an employee requires equipment.

A request can be created by an employee, technician, manager, or administrator depending on the organisation's workflow.

## Request Information

A request contains:

- Request ID
- Requested By
- Employee receiving the device
- Department
- Manager
- Device Requested
- Reason
- Priority
- Requested specifications
- Additional notes
- Date requested
- Current status

Example:

```text
Request ID: REQ1028

Requested By:
Tom Harris

Department:
Sales

Device:
Dell Latitude 7455

Reason:
New starter

Priority:
High

Specifications:
16GB RAM
512GB SSD
Windows 11 Pro

Status:
Pending Approval
```

---

# 5. Request Status

A request can have the following statuses:

```text
Pending Approval
Approved
Rejected
Cancelled
Ordered
Received
Completed
```

The status should always be visible to the user.

---

# 6. Approval Process

Once a request has been submitted, it enters the approval queue.

The approver can open the request and view:

- Employee
- Department
- Device requested
- Reason
- Estimated cost
- Priority
- Supporting documents
- Previous approval activity

The approver can then:

```text
Approve
Reject
Request More Information
```

If approved, the system records:

```text
Approved By
Approval Date
Approval Comments
Budget / Cost Centre
```

Example:

```text
Request:
REQ1028

Status:
Approved

Approved By:
Emily Carter

Approved Date:
16 July 2026

Comments:
Approved for new starter.
```

The approval should become part of the permanent request history.

---

# 7. Rejected Requests

If a request is rejected, the approver must provide a reason.

Example:

```text
Status:
Rejected

Rejected By:
Emily Carter

Reason:
Existing laptop available in inventory.
```

The request remains in the system for historical and reporting purposes.

It should not simply disappear.

---

# 8. Purchasing

Once a request has been approved, the IT or procurement team can record purchasing information.

Information includes:

- Supplier
- Purchase order number
- Cost
- Order date
- Expected delivery date
- Tracking number
- Purchase notes

Example:

```text
Supplier:
Dell

PO Number:
PO33445

Cost:
$1,849.00

Ordered:
12 July 2026

Expected Delivery:
18 July 2026

Status:
Ordered
```

---

# 9. Receiving

When the physical device arrives, a technician can mark the request as received.

The technician records:

- Received date
- Received by
- Physical condition
- Serial number
- Asset tag
- Manufacturer
- Model
- Warranty information
- Purchase information
- Photos
- Notes

At this stage the system can create the permanent asset record.

---

# 10. Asset Creation

Each physical device receives its own asset record.

An asset should contain:

## Identification

```text
Asset ID
Asset Tag
Serial Number
Manufacturer
Model
Device Type
```

## Status

```text
Available
Assigned
In Repair
Lost
Stolen
Retired
Disposed
```

## Ownership

```text
Current User
Department
Location
```

## IT Information

```text
Assigned Technician
Ticket Number
Purchase Date
Warranty Expiry
```

## Additional Information

```text
Notes
Photos
Attachments
QR Code
```

---

# 11. Asset Example

```text
Dell Latitude 7450

Asset Tag:
IT-00123

Serial Number:
7JK3L92

Category:
Laptop

Status:
Assigned

Assigned To:
Harry Ward

Location:
Melbourne Office

Assigned By:
John Smith

Ticket:
INC54321

Warranty:
10 January 2030
```

---

# 12. Asset Assignment

When an asset is ready to be issued, a technician selects:

```text
Employee
Asset
Technician
Ticket Number
Location
Accessories
Notes
```

The technician confirms the assignment.

The system then:

1. Changes the asset status to `Assigned`.
2. Links the asset to the employee.
3. Records the technician.
4. Records the ticket number.
5. Records the assignment date.
6. Creates an audit event.
7. Adds the assignment to the asset timeline.

Example:

```text
Laptop IT-00123

Assigned To:
Harry Ward

Assigned By:
John Smith

Ticket:
INC54321

Date:
16 July 2026
```

---

# 13. Accessories

Assets can have associated accessories.

For example:

```text
Laptop
 ├── Dock
 ├── Monitor
 ├── Keyboard
 ├── Mouse
 ├── Headset
 └── Charger
```

Each accessory can either have its own asset record or be tracked as part of an equipment bundle.

This makes it possible to identify missing equipment when an employee returns their devices.

---

# 14. Asset Timeline

Every asset should have a chronological timeline.

Example:

```text
16 Jul
Assigned to Harry Ward
Technician: John Smith
Ticket: INC54321

14 Jul
Asset received
Technician: Harry Ward

12 Jul
Purchase order created
PO: PO33445

11 Jul
Request approved
Approved by: Emily Carter

10 Jul
Request submitted
Requested by: Tom Harris
```

The timeline should be append-only.

Historical events should not be overwritten when the asset changes.

---

# 15. Ticket Tracking

IT actions should be linked to support tickets whenever possible.

A ticket can be associated with:

- Request
- Assignment
- Return
- Repair
- Replacement
- Disposal

Example:

```text
Ticket:
INC54321

Action:
Laptop Assigned

Technician:
John Smith

Date:
16 July 2026
```

The application should allow users to search for an asset using its ticket number.

---

# 16. Asset Returns

When an employee returns an asset, the technician starts the return process.

The technician records:

- Return date
- Returned by
- Received by
- Condition
- Ticket number
- Accessories returned
- Missing accessories
- Notes
- Photos

Example:

```text
Returned By:
Harry Ward

Received By:
John Smith

Condition:
Good

Missing:
Laptop charger

Ticket:
INC55421
```

The asset can then become:

```text
Ready to Deploy
```

or:

```text
In Repair
```

depending on its condition.

---

# 17. Repairs

If an asset has a fault, the technician can place it into repair.

The repair record includes:

```text
Fault
Diagnosis
Repair Performed
Technician
Ticket Number
Repair Cost
Repair Vendor
Date Sent
Date Returned
Notes
```

Example:

```text
Asset:
IT-00123

Fault:
Laptop will not charge

Diagnosis:
Damaged charging port

Repair:
Charging port replaced

Technician:
John Smith

Ticket:
INC56111

Status:
Repaired
```

After the repair is completed, the asset can return to:

```text
Ready to Deploy
```

---

# 18. Lost or Stolen Assets

Assets can be marked as:

```text
Lost
Stolen
```

When this occurs, the system records:

- Date
- Reported by
- Technician
- Employee
- Ticket
- Circumstances
- Notes

The asset remains in the database and retains its complete history.

---

# 19. Asset Retirement

When an asset reaches the end of its useful life, it can be marked:

```text
Retired
```

The system should record:

- Retirement date
- Reason
- Technician
- Ticket
- Final location
- Data wiping confirmation
- Disposal information

The asset should no longer be available for normal assignment.

---

# 20. Asset Disposal

The final lifecycle stage is disposal.

The system can record:

```text
Disposal Date
Disposal Method
Disposal Company
Certificate Number
Technician
Ticket
Notes
```

Example:

```text
Asset:
IT-00087

Status:
Disposed

Disposal Method:
E-Waste Recycling

Disposed:
20 August 2026

Certificate:
EW-88321
```

The record remains available for auditing.

---

# 21. Dashboard

The dashboard provides an overview of the IT environment.

## Summary Cards

```text
Total Assets
Assigned
Available
Pending Approval
Ready to Deploy
In Repair
Retired
```

## Other Information

The dashboard can display:

- Recent requests
- Pending approvals
- Recently assigned assets
- Recently returned assets
- Warranty expirations
- Assets in repair
- Recent technician activity

---

# 22. Asset Search

The system should have global search.

Users can search by:

```text
Asset Tag
Serial Number
Employee
Ticket Number
Request ID
Model
Manufacturer
Technician
```

Example:

Searching:

```text
7JK3L92
```

could return:

```text
IT-00123
Dell Latitude 7450
Assigned to Harry Ward
Ticket INC54321
```

---

# 23. QR Codes

Each asset receives a unique QR code.

A technician can scan the QR code using their phone and immediately open the asset.

For example:

```text
Scan QR
   ↓
IT-00123
   ↓
Asset Details
   ↓
Assign / Return / Repair / Edit
```

This is particularly useful when technicians are physically handling large amounts of equipment.

---

# 24. Notifications

The system can notify users about important events.

Examples:

```text
New device request submitted.

Request awaiting approval.

Device approved.

Device received.

Device ready for deployment.

Asset returned.

Asset requires repair.

Warranty expires in 30 days.

Asset has been overdue for return.
```

---

# 25. Audit Log

The system should maintain a complete audit log.

Every important action records:

```text
User
Action
Date / Time
Asset
Request
Ticket
Previous Value
New Value
```

Example:

```text
John Smith

Changed Asset Status

From:
Available

To:
Assigned

Asset:
IT-00123

Ticket:
INC54321

16 July 2026 10:23 AM
```

This provides accountability and makes troubleshooting much easier.

---

# 26. Reporting

The reporting section provides useful operational information.

Reports can include:

### Asset Inventory

```text
All assets
Available assets
Assigned assets
Retired assets
```

### Requests

```text
Requests submitted
Approved
Rejected
Pending
Average approval time
```

### Repairs

```text
Assets repaired
Repair frequency
Repair costs
Average repair time
```

### Warranty

```text
Expiring soon
Expired
Active warranties
```

### Technician Activity

```text
Assignments
Returns
Repairs
Requests processed
```

Reports should be exportable to CSV or Excel.

---

# 27. Employee Profile

Each employee has a profile containing:

```text
Name
Email
Department
Manager
Location
Status
```

The profile shows:

### Assigned Assets

```text
Laptop
Monitor
Dock
Phone
Headset
```

### Requests

```text
Current requests
Previous requests
Approved requests
Rejected requests
```

### History

All relevant asset activity involving the employee.

---

# 28. Employee Offboarding

When an employee leaves the organisation, the system can create an offboarding workflow.

Example:

```text
Employee Leaving
       ↓
Find Assigned Assets
       ↓
Return Laptop
       ↓
Return Monitor
       ↓
Return Phone
       ↓
Check Accessories
       ↓
Record Condition
       ↓
Complete Offboarding
```

The system should clearly identify anything that has not been returned.

---

# 29. Employee Onboarding

The same system can assist with onboarding.

Example:

```text
New Employee
      ↓
Device Request
      ↓
Approval
      ↓
Device Procurement
      ↓
Device Received
      ↓
Device Configuration
      ↓
Assignment
      ↓
Employee Starts
```

This gives IT a single workflow instead of managing the process across spreadsheets, emails and ticket systems.

---

# 30. Complete Example

A typical laptop lifecycle would look like this:

```text
DAY 1

Tom requests a laptop.

        ↓

REQUESTED

REQ1028 created.

        ↓

PENDING APPROVAL

Manager receives notification.

        ↓

APPROVED

Emily approves the request.

        ↓

ORDERED

IT orders the Dell laptop.

        ↓

RECEIVED

Laptop arrives.

Serial number:
7JK3L92

Asset tag:
IT-00123

        ↓

READY TO DEPLOY

Technician configures the device.

        ↓

ASSIGNED

Laptop assigned to Tom.

Ticket:
INC54321

Technician:
John Smith

        ↓

IN USE

Tom uses the laptop.

        ↓

REPAIR

Laptop develops a charging fault.

Ticket:
INC56111

        ↓

REPAIRED

Charging port replaced.

        ↓

READY TO DEPLOY

        ↓

ASSIGNED

Laptop assigned again.

        ↓

RETURNED

Employee leaves the company.

        ↓

READY TO DEPLOY

        ↓

RETIRED

Laptop reaches end of life.

        ↓

DISPOSED

Laptop sent to approved e-waste recycling.
```

Every step remains visible in the asset's history.

---

# 31. Core Design Principle

The application should treat **Requests** and **Assets** as related but separate concepts.

A request represents:

> "Someone needs a device."

An asset represents:

> "This specific physical device exists."

For example:

```text
REQUEST

REQ1028
Dell Latitude 7455
Requested for Tom Harris
Approved by Emily Carter


             ↓


ASSET

IT-00123
Serial: 7JK3L92
Dell Latitude 7455
Assigned to Tom Harris
```

This distinction is important because the requested device may not always become the exact physical device eventually issued.

---

# 32. Main Navigation

The application should have a simple navigation structure:

```text
Dashboard

Assets
   ├── All Assets
   ├── Available
   ├── Assigned
   ├── In Repair
   └── Retired

Requests
   ├── All Requests
   ├── My Requests
   ├── Pending Approval
   ├── Approved
   └── Rejected

Users

Tickets

Reports

Locations

Settings
```

---

# 33. The Overall System

The application ultimately becomes a central source of truth for IT equipment.

```text
                    ┌──────────────┐
                    │   Employee   │
                    └──────┬───────┘
                           │
                       Requests
                           ↓
                  ┌─────────────────┐
                  │     Request     │
                  └────────┬────────┘
                           │
                       Approval
                           ↓
                  ┌─────────────────┐
                  │    Purchasing   │
                  └────────┬────────┘
                           │
                       Received
                           ↓
                  ┌─────────────────┐
                  │      Asset      │
                  └────────┬────────┘
                           │
                       Assignment
                           ↓
                  ┌─────────────────┐
                  │     Employee    │
                  └────────┬────────┘
                           │
                    Return / Repair
                           ↓
                  ┌─────────────────┐
                  │ Asset Lifecycle │
                  └────────┬────────┘
                           │
                     Retirement
                           ↓
                  ┌─────────────────┐
                  │    Disposal     │
                  └─────────────────┘
```

The key goal is that **no important IT asset event happens without being recorded**.

That gives the IT team a complete, searchable history of **what equipment exists, who has it, why they have it, who actioned it, which ticket relates to it, where it is, what happened to it, and where it is in its lifecycle.**
