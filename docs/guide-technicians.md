# GIIM for technicians

GIIM is where the service desk records who has which device, gets starters ready and gets equipment back from
leavers. Open it from the **GIIM tile in My Apps**. Your access comes from the GIIM-Technicians group; if you see
"No access to GIIM", ask an administrator.

Every action you take is recorded with your name and the time, and can't be edited or deleted afterwards. Put the
**ticket number** on actions whenever there is one: it links the device's history to ServiceDesk Plus.

## What needs you

The **bell** at the top shows what's waiting for IT: devices approved and ready to order or hand over, and starters
and leavers in the next 3 days (red when one is overdue). Administrators also see checklist steps waiting for their
approval, ServiceDesk tickets GIIM couldn't use, failed syncs and emails that didn't send. Click an item to open it;
it disappears once it's dealt with. The count also shows in the browser tab, e.g. **(3) GIIM**.

## Finding things

- **Search** (top bar): a name, serial, asset tag, ticket number or REQ number. An exact serial, tag or ticket opens
  it straight away. Spaces and dashes in serials don't matter.
- **Scanning**: with a 2D scanner, click the search box and scan the label or the manufacturer's serial barcode. With a
  phone, point the camera at the GIIM label; it opens the device (you sign in on the phone the first time).
- **Tickets** shows everything recorded against each ticket.

## Starters

A starter ticket in ServiceDesk Plus creates a checklist in GIIM within a minute or two, under
**Starters and leavers**. (If the ticket is missing a start date or department, GIIM flags it for an administrator
instead.) You can also add one by hand with **New starter**.

The checklist comes from the starter's department profile:

| Task | What to do |
|---|---|
| **Allocate and scan: Laptop** (and other devices) | Linked to a device request (REQ-…), raised automatically. Once the manager approves it, hand over a device from stock or order one (see below). The task ticks itself off at handover |
| **Create AD account**, **Enable remote mailbox**, **Add to group**, **Enable the account** | Tagged *can run automatically*: use **Run automated steps** in the **Automation** panel (below). Or do them by hand and **Mark done**, as before |
| **Grant app** (without an access group), and anything tagged *automated later* | Do it by hand, then **Mark done** |
| **Issue from stock** | Issue the item on the **Stock** page (so the count goes down), then **Mark done** |

**Skip** a task that isn't needed; GIIM asks why. **Reopen** undoes a tick. Each step adds a note to the ticket, and
the ticket can be resolved automatically when the checklist is finished.

**Automation** (starter checklists): the panel on the right shows whether the on-prem agent is connected. **Run
automated steps** shows exactly what will happen, then GIIM and the agent create the account (disabled), the mailbox
and the groups, and enable the account at 06:00 on the start date; each task ticks itself off. While **dry run** is
on, nothing is changed: each step only says what it would do, and the tasks come back to you. If a step fails, the
task shows why: fix the cause (e.g. ask for the missing group) and **Retry**, or **Stop and do by hand**. GIIM never
sees the starter's password; give them a Temporary Access Pass on their first morning.

## Leavers

A leaver ticket creates a checklist from **what the person actually holds** in GIIM, not from their department.

- **Recover** tasks: one per device. When the device comes back, **Return** it on the device's page (below). The
  task ticks itself off. If it was lost or stolen, **Report lost / Report stolen** instead; that also closes the task.
- **Access removal** and other destructive steps need an **administrator to approve** them first, and must then be done
  by someone other than the approver.
- The leaver's manager is emailed the list of equipment to return. If items are still out after the last day, they
  get a reminder the next day, then weekly (three at most). Items still out also show in the IT team's daily email.

## Devices

Open a device from **Assets** or search. The buttons offered depend on where the device is in its lifecycle:

```
Received → Ready to deploy → Assigned → (Return requested) → Returned → Wiped → Ready to deploy …
                                    ↘ In repair ↗           ↘ Retired → Disposed
                         Lost / Stolen → Recovered (comes back as Returned, so it's wiped before reuse)
```

| Button | When |
|---|---|
| **Add asset** (Assets page) | A device that isn't in GIIM yet. Scan the serial; GIIM warns about duplicates |
| **Assign** | Ready to deploy → someone. Add the accessories that go with it (dock, charger, bag) so they're checked back in on return |
| **Request return** | You want it back (e.g. a leaver); optional due date |
| **Return** | It's back. Record the condition and tick the accessories that came back; anything not ticked is recorded as missing |
| **Record wipe** | Returned devices must be wiped before reuse. Record how (Intune wipe, Autopilot reset, reimaged) |
| **Mark ready to deploy** | Set up and ready for the next person |
| **Send to repair / Complete repair** | Internal fix or warranty claim. Record the fault, vendor, cost and result |
| **Report lost / Report stolen** | Record the circumstances (and police reference). The person no longer holds it; their history keeps it |
| **Recovered** | A lost or stolen device turned up |
| **Retire**, then **Record disposal** | End of life. Record how the data was dealt with, then the disposal method and certificate |
| **Move**, **Add note**, **Print label** | Any time. Print a sheet of labels from **Assets** by ticking several devices |

**Files** (on the device page, under the buttons): keep the invoice, warranty documents, repair reports and photos
with the device. **Add files** takes photos (JPG, PNG, HEIC: on a phone it offers the camera), PDFs, Word, Excel,
Outlook messages and text files, up to 20 MB each. The **Return** form can take photos of damage, and **Record
disposal** the recycler's certificate. **Remove** asks why; the file is hidden but kept, and the timeline shows both.

If someone changed the device while you had it open, GIIM refuses the action and asks you to refresh, so two people
can't return the same laptop twice.

## Device requests

Under **Requests**. Once a request is **Approved**:

1. **Hand over from stock** if there's a suitable device ready to deploy, or
2. **Record order** (supplier, PO number, expected delivery), then **Receive device** when it arrives (this creates the
   asset record), then **Hand over**.

The requester is emailed at each step, and the starter's checklist task ticks off at handover.

## Intune reconciliation

**Intune reconciliation** compares GIIM with Intune (synced every 4 hours) and lists devices in Intune but not in
GIIM, devices GIIM thinks are in use that Intune hasn't seen, devices not seen for 90+ days, and devices used by
someone other than the person GIIM has them against. Work through it like a queue; it's the quickest way to find
devices the register has wrong.

## Stock

**Stock** is for things without serial numbers (chargers, mice, headsets). Issue them on the Stock page, or as accessories when you
assign a device, so the counts stay right. Ticking a checklist task doesn't change the count.

## Who to ask

| Problem | Ask |
|---|---|
| Can't sign in, or wrong access | A GIIM administrator (Entra group membership) |
| A starter or leaver ticket didn't create a checklist | A GIIM administrator (Setup → ServiceDesk Plus shows why) |
| A department's starter checklist is wrong | A GIIM administrator (Setup → Starter profiles) |
