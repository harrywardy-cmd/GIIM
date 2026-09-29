import { useEffect, useState } from 'react'
import { Download, Printer } from 'lucide-react'
import { api } from '../api'
import { LabelSheet } from './LabelSheet'
import { StatusBadge } from '../StatusBadge'
import { LocationSelect } from '../LocationSelect'
import { useNav } from '../nav'
import { useUser } from '../user'
import { statusText } from '../status'
import { AssignForm, ReturnForm, type AccessoryLine } from './AssignReturnForms'
import { CompleteRepairForm, DisposeForm, RetireForm, SendToRepairForm } from './RepairEndOfLifeForms'

type TimelineEvent = {
  id: number
  occurredAt: string
  type: string
  fromStatus: string | null
  toStatus: string | null
  actor: string
  ticketNumber: string | null
  summary: string
  note: string | null
  details: Record<string, unknown> | null
}

type AssetDetails = {
  id: string
  assetTag: string | null
  serialNumber: string
  manufacturer: string
  model: string
  category: string | null
  isIntuneManaged: boolean | null
  status: string
  location: string | null
  purchaseDate: string | null
  warrantyExpiry: string | null
  supplier: string | null
  notes: string | null
  legacyAssignedTo: string | null
  assignedTo: { id: string; displayName: string; userPrincipalName: string | null; department: string | null; status: string } | null
  lastSeenInIntune: string | null
  nextStatuses: string[]
  repairs: {
    id: string
    fault: string
    vendor: string | null
    warrantyClaim: boolean
    vendorReference: string | null
    openedAt: string
    openedBy: string
    ticketNumber: string | null
    diagnosis: string | null
    workPerformed: string | null
    cost: number | null
    outcome: string | null
    completedAt: string | null
    isOpen: boolean
    durationDays: number | null
  }[]
  endOfLife: {
    retiredAt: string
    retirementReason: string
    dataSanitisation: string
    disposedOn: string | null
    disposalMethod: string | null
    disposalCompany: string | null
    disposalCertificate: string | null
  } | null
  owners: {
    id: string
    personId: string
    displayName: string
    assignedAt: string
    endedAt: string | null
    assignedBy: string | null
    receivedBy: string | null
    ticketNumber: string | null
    returnTicketNumber: string | null
    returnCondition: string | null
    missing: string[]
  }[]
  currentAssignment: {
    id: string
    assignedAt: string
    assignedBy: string | null
    ticketNumber: string | null
    notes: string | null
    accessories: AccessoryLine[]
  } | null
  timeline: TimelineEvent[]
}

type Action = 'Assign' | 'Return' | 'RequestReturn' | 'SendToRepair' | 'CompleteRepair' | 'Retire' | 'Dispose' | 'MarkReady' | 'MarkWiped' | 'ReportLost' | 'ReportStolen' | 'Recover' | 'Move' | 'AddNote'

/** Which actions are offered depends on where the asset is in its lifecycle. */
function availableActions(a: AssetDetails): { action: Action; label: string }[] {
  const next = new Set(a.nextStatuses)
  const actions: { action: Action; label: string }[] = []
  if (a.status === 'ReadyToDeploy') actions.push({ action: 'Assign', label: 'Assign' })
  const openRepair = a.repairs.some((r) => r.isOpen)
  if (a.status === 'Assigned' || a.status === 'ReturnRequested' || (a.status === 'InRepair' && a.assignedTo))
    actions.push({ action: 'Return', label: 'Return' })
  if (openRepair) actions.push({ action: 'CompleteRepair', label: 'Complete repair' })
  else if (next.has('InRepair')) actions.push({ action: 'SendToRepair', label: 'Send to repair' })
  if (next.has('ReturnRequested')) actions.push({ action: 'RequestReturn', label: 'Request return' })
  if (next.has('ReadyToDeploy')) actions.push({ action: 'MarkReady', label: 'Mark ready to deploy' })
  if (next.has('Wiped')) actions.push({ action: 'MarkWiped', label: 'Record wipe' })
  if (a.status === 'Lost' || a.status === 'Stolen') actions.push({ action: 'Recover', label: 'Recovered' })
  if (next.has('Lost')) actions.push({ action: 'ReportLost', label: 'Report lost' })
  if (next.has('Stolen')) actions.push({ action: 'ReportStolen', label: 'Report stolen' })
  if (next.has('Retired') && !a.assignedTo && !openRepair) actions.push({ action: 'Retire', label: 'Retire' })
  if (a.status === 'Retired') actions.push({ action: 'Dispose', label: 'Record disposal' })
  if (a.status !== 'Disposed') actions.push({ action: 'Move', label: 'Move' })
  actions.push({ action: 'AddNote', label: 'Add note' })
  return actions
}

const detailLabels: Record<string, string> = {
  Method: 'Method',
  Circumstances: 'Circumstances',
  ReportedBy: 'Reported by',
  PoliceReference: 'Police reference',
  WhereFound: 'Where found',
  Location: 'Location',
  Accessories: 'Accessories',
  IssuedWith: 'Issued with',
  DueDate: 'Due',
  Condition: 'Condition',
  ReturnedBy: 'Returned by',
  PreviousHolder: 'Returned from',
  Missing: 'Missing',
  LegacyName: 'Spreadsheet name',
  MatchedBy: 'Matched by',
  Fault: 'Fault',
  Vendor: 'Vendor',
  WarrantyClaim: 'Warranty claim',
  VendorReference: 'Vendor ref',
  Outcome: 'Outcome',
  Diagnosis: 'Diagnosis',
  WorkPerformed: 'Work',
  Cost: 'Cost',
  Reason: 'Reason',
  DataSanitisation: 'Data',
  FinalLocation: 'Final location',
  Company: 'Company',
  Certificate: 'Certificate',
  DisposedOn: 'Disposed on',
}

/** Internal ids and names already in the summary are not repeated in the timeline. */
const hiddenDetails = new Set(['PersonId', 'PreviousHolderId', 'PersonName', 'RepairId'])

const detailText = (v: unknown): string =>
  Array.isArray(v) ? v.join(', ') : typeof v === 'boolean' ? (v ? 'Yes' : 'No') : String(v ?? '')

const simpleActions: Action[] = ['MarkReady', 'MarkWiped', 'ReportLost', 'ReportStolen', 'Recover', 'RequestReturn', 'Move', 'AddNote']

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')

export function AssetDetailsPage({ assetId, onBack }: { assetId: string; onBack: () => void }) {
  const [asset, setAsset] = useState<AssetDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [action, setAction] = useState<Action | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [printing, setPrinting] = useState(false)
  const nav = useNav()
  const { canChange } = useUser()
  const ticketLink = (ticket: string | null) =>
    ticket ? (
      <button className="link" onClick={() => nav.openTicket(ticket)}>
        {ticket}
      </button>
    ) : (
      '-'
    )
  const done = () => {
    setAction(null)
    setRefresh((n) => n + 1)
  }

  useEffect(() => {
    api<AssetDetails>(`/api/assets/${assetId}`)
      .then((a) => {
        setAsset(a)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [assetId, refresh])

  if (!asset) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  if (printing) return <LabelSheet assetIds={[asset.id]} onBack={() => setPrinting(false)} />
  const openRepair = asset.repairs.find((r) => r.isOpen)

  return (
    <>
      <button onClick={onBack}>← Back to assets</button>
      <div className="details-header">
        <div>
          <h2>
            {asset.manufacturer} {asset.model}
          </h2>
          <p className="muted">
            {asset.assetTag ?? 'No asset tag'} · S/N {asset.serialNumber} · {asset.category}
          </p>
        </div>
        <StatusBadge status={asset.status} />
      </div>

      <div className="details-grid">
        <section className="panel">
          <h3>Details</h3>
          <dl className="facts">
            <dt>Location</dt>
            <dd>{asset.location ?? '-'}</dd>
            <dt>Assigned to</dt>
            <dd>
              {asset.assignedTo ? (
                <>
                  <button className="link" onClick={() => nav.openPerson(asset.assignedTo!.id)}>
                    {asset.assignedTo.displayName}
                  </button>
                  <div className="muted small">
                    {asset.assignedTo.userPrincipalName} · {asset.assignedTo.department}
                    {asset.assignedTo.status !== 'Active' && ` · ${asset.assignedTo.status}`}
                  </div>
                </>
              ) : asset.legacyAssignedTo ? (
                <span className="muted">{asset.legacyAssignedTo} (from spreadsheet, not yet linked)</span>
              ) : (
                '-'
              )}
            </dd>
            {asset.currentAssignment && (
              <>
                <dt>Assigned</dt>
                <dd>
                  {formatDate(asset.currentAssignment.assignedAt)}
                  <div className="muted small">
                    by {asset.currentAssignment.assignedBy ?? '-'}
                    {asset.currentAssignment.ticketNumber && <> · ticket {asset.currentAssignment.ticketNumber}</>}
                  </div>
                </dd>
                <dt>Accessories</dt>
                <dd>
                  {asset.currentAssignment.accessories.length === 0
                    ? 'None recorded'
                    : asset.currentAssignment.accessories.map((x) => (
                        <div key={x.id} className="small">
                          ✓ {x.label}
                        </div>
                      ))}
                </dd>
              </>
            )}
            <dt>Purchased</dt>
            <dd>{formatDate(asset.purchaseDate)}</dd>
            <dt>Warranty expires</dt>
            <dd>{formatDate(asset.warrantyExpiry)}</dd>
            <dt>Supplier</dt>
            <dd>{asset.supplier ?? '-'}</dd>
            <dt>Last seen in Intune</dt>
            <dd>{asset.isIntuneManaged ? formatDate(asset.lastSeenInIntune) : 'Not managed in Intune'}</dd>
          </dl>

          <div className="qr-card">
            <img src={`/api/assets/${asset.id}/qr.svg`} alt={`QR code for ${asset.assetTag ?? asset.serialNumber}`} />
            <div className="actions">
              <span className="muted small">Scan with a phone camera or 2D scanner to open this asset.</span>
              <a className="button" href={`/api/assets/${asset.id}/qr.png`} download>
                <Download size={16} /> Download QR
              </a>
              {canChange && (
                <button onClick={() => setPrinting(true)}>
                  <Printer size={16} /> Print label
                </button>
              )}
            </div>
          </div>

          <h3>Actions</h3>
          {notice && <p className="success">{notice}</p>}
          {!canChange && <p className="muted small">You have read-only access.</p>}
          <div className="action-buttons">
            {(canChange ? availableActions(asset) : []).map((a) => (
              <button key={a.action} className={action === a.action ? 'primary' : ''} onClick={() => setAction(a.action)}>
                {a.label}
              </button>
            ))}
          </div>

          {action === 'Assign' && (
            <AssignForm
              assetId={asset.id}
              expectedStatus={asset.status}
              onDone={() => {
                setAction(null)
                setRefresh((n) => n + 1)
              }}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'Return' && (
            <ReturnForm
              assetId={asset.id}
              expectedStatus={asset.status}
              holderName={asset.assignedTo?.displayName ?? asset.legacyAssignedTo}
              accessories={asset.currentAssignment?.accessories ?? []}
              onDone={(missing) => {
                setAction(null)
                setNotice(missing.length ? `Returned. Missing: ${missing.join(', ')}` : 'Returned with everything accounted for.')
                setRefresh((n) => n + 1)
              }}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'SendToRepair' && (
            <SendToRepairForm assetId={asset.id} expectedStatus={asset.status} onDone={done} onCancel={() => setAction(null)} />
          )}
          {action === 'CompleteRepair' && openRepair && (
            <CompleteRepairForm assetId={asset.id} repairId={openRepair.id} onDone={done} onCancel={() => setAction(null)} />
          )}
          {action === 'Retire' && <RetireForm assetId={asset.id} status={asset.status} onDone={done} onCancel={() => setAction(null)} />}
          {action === 'Dispose' && <DisposeForm assetId={asset.id} onDone={done} onCancel={() => setAction(null)} />}
          {action && simpleActions.includes(action) && (
            <ActionForm
              key={action}
              asset={asset}
              action={action}
              onDone={() => {
                setAction(null)
                setRefresh((n) => n + 1)
              }}
              onCancel={() => setAction(null)}
            />
          )}
        </section>

        <section className="panel">
          <h3>Owners ({asset.owners.length})</h3>
          {asset.owners.length === 0 ? (
            <p className="muted small">Never assigned.</p>
          ) : (
            <table className="owners">
              <thead>
                <tr>
                  <th>Person</th>
                  <th>From</th>
                  <th>To</th>
                  <th>Tickets</th>
                </tr>
              </thead>
              <tbody>
                {asset.owners.map((o) => (
                  <tr key={o.id}>
                    <td>
                      <button className="link" onClick={() => nav.openPerson(o.personId)}>
                        {o.displayName}
                      </button>
                      <div className="muted small">issued by {o.assignedBy ?? '-'}</div>
                    </td>
                    <td>{formatDate(o.assignedAt)}</td>
                    <td>
                      {o.endedAt ? formatDate(o.endedAt) : <strong>Current</strong>}
                      {o.returnCondition && <div className="muted small">{o.returnCondition.toLowerCase()}, received by {o.receivedBy}</div>}
                      {o.missing.length > 0 && <div className="error small">Missing: {o.missing.join(', ')}</div>}
                    </td>
                    <td className="small">
                      {ticketLink(o.ticketNumber)}
                      {o.returnTicketNumber && <div>{ticketLink(o.returnTicketNumber)}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {asset.endOfLife && (
            <>
              <h3>End of life</h3>
              <dl className="facts">
                <dt>Retired</dt>
                <dd>
                  {formatDate(asset.endOfLife.retiredAt)}: {asset.endOfLife.retirementReason}
                </dd>
                <dt>Data</dt>
                <dd>{asset.endOfLife.dataSanitisation}</dd>
                {asset.endOfLife.disposedOn && (
                  <>
                    <dt>Disposed</dt>
                    <dd>
                      {formatDate(asset.endOfLife.disposedOn)} · {asset.endOfLife.disposalMethod}
                      {asset.endOfLife.disposalCompany && <> · {asset.endOfLife.disposalCompany}</>}
                    </dd>
                    <dt>Certificate</dt>
                    <dd>{asset.endOfLife.disposalCertificate ?? '-'}</dd>
                  </>
                )}
              </dl>
            </>
          )}

          {asset.repairs.length > 0 && (
            <>
              <h3>Repairs ({asset.repairs.length})</h3>
              <table className="owners">
                <thead>
                  <tr>
                    <th>Fault</th>
                    <th>By</th>
                    <th>Result</th>
                    <th>Cost</th>
                  </tr>
                </thead>
                <tbody>
                  {asset.repairs.map((r) => (
                    <tr key={r.id}>
                      <td>
                        {r.fault}
                        <div className="muted small">
                          {formatDate(r.openedAt)} · {r.ticketNumber ?? 'no ticket'}
                        </div>
                      </td>
                      <td className="small">
                        {r.vendor ?? 'IT (internal)'}
                        {r.warrantyClaim && <div>warranty</div>}
                        {r.vendorReference && <div className="muted">{r.vendorReference}</div>}
                      </td>
                      <td className="small">
                        {r.isOpen ? (
                          <strong>In progress</strong>
                        ) : (
                          <>
                            {r.outcome === 'BeyondRepair' ? 'Beyond repair' : r.workPerformed}
                            <div className="muted">
                              {r.diagnosis}
                              {r.durationDays !== null && <> · {r.durationDays} days</>}
                            </div>
                          </>
                        )}
                      </td>
                      <td className="small">{r.cost !== null ? '$' + r.cost.toFixed(2) : '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          <h3>Timeline</h3>
          <ol className="timeline">
            {asset.timeline.map((e) => (
              <li key={e.id}>
                <div className="timeline-when">{new Date(e.occurredAt).toLocaleString('en-AU')}</div>
                <div className="timeline-what">
                  <strong>{e.summary}</strong>
                  {e.fromStatus && e.toStatus && (
                    <span className="muted small">
                      {' '}
                      {statusText(e.fromStatus)} → {statusText(e.toStatus)}
                    </span>
                  )}
                </div>
                <div className="muted small">
                  by{' '}
                  <button className="link" onClick={() => nav.openTechnician(e.actor)}>
                    {e.actor}
                  </button>
                  {e.ticketNumber && <> · ticket {ticketLink(e.ticketNumber)}</>}
                </div>
                {e.details &&
                  Object.entries(e.details)
                    .filter(([k, v]) => v !== null && v !== false && v !== '' && !hiddenDetails.has(k) && !(Array.isArray(v) && v.length === 0))
                    .map(([k, v]) => (
                      <div key={k} className="small">
                        {detailLabels[k] ?? k}: {detailText(v)}
                      </div>
                    ))}
                {e.note && <div className="small timeline-note">{e.note}</div>}
              </li>
            ))}
          </ol>
        </section>
      </div>
    </>
  )
}

function ActionForm({
  asset,
  action,
  onDone,
  onCancel,
}: {
  asset: AssetDetails
  action: Action
  onDone: () => void
  onCancel: () => void
}) {
  const [ticketNumber, setTicketNumber] = useState('')
  const [note, setNote] = useState('')
  const [method, setMethod] = useState('Intune wipe')
  const [circumstances, setCircumstances] = useState('')
  const [reportedBy, setReportedBy] = useState('')
  const [policeReference, setPoliceReference] = useState('')
  const [whereFound, setWhereFound] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [locationId, setLocationId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setSaving(true)
    try {
      await api(`/api/assets/${asset.id}/actions`, {
        method: 'POST',
        json: {
          action,
          expectedStatus: asset.status,
          ticketNumber: ticketNumber || null,
          note: note || null,
          method,
          circumstances,
          reportedBy: reportedBy || null,
          policeReference: policeReference || null,
          whereFound,
          dueDate: dueDate || null,
          locationId: locationId || null,
        },
      })
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="action-form">
      <div className="form-row">
        {action === 'MarkWiped' && (
          <label>
            Wipe method
            <select value={method} onChange={(e) => setMethod(e.target.value)}>
              <option>Intune wipe</option>
              <option>Autopilot reset</option>
              <option>Reimaged</option>
              <option>Factory reset (phone / tablet)</option>
            </select>
          </label>
        )}
        {(action === 'ReportLost' || action === 'ReportStolen') && (
          <>
            <label className="grow">
              Circumstances (required)
              <input value={circumstances} onChange={(e) => setCircumstances(e.target.value)} />
            </label>
            <label>
              Reported by
              <input value={reportedBy} onChange={(e) => setReportedBy(e.target.value)} />
            </label>
          </>
        )}
        {action === 'ReportStolen' && (
          <label>
            Police reference
            <input value={policeReference} onChange={(e) => setPoliceReference(e.target.value)} />
          </label>
        )}
        {action === 'Move' && (
          <label>
            Moved to (required)
            <LocationSelect value={locationId} onChange={setLocationId} />
          </label>
        )}
        {action === 'RequestReturn' && (
          <label>
            Due back by
            <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
          </label>
        )}
        {action === 'Recover' && (
          <label className="grow">
            Where / how recovered (required)
            <input value={whereFound} onChange={(e) => setWhereFound(e.target.value)} />
          </label>
        )}
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} placeholder="e.g. INC54321" />
        </label>
        <label className="grow">
          Note{action === 'AddNote' && ' (required)'}
          <input value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
      </div>
      {action === 'ReportStolen' && (
        <p className="muted small">Consider an Intune remote wipe for stolen laptops and phones.</p>
      )}
      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={submit} disabled={saving}>
        {saving ? 'Saving…' : 'Save'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </div>
  )
}
