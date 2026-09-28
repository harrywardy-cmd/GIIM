import { useEffect, useState } from 'react'
import { api } from '../api'
import { StatusBadge } from '../StatusBadge'
import { statusText } from '../status'

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
  details: Record<string, string | null> | null
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
  timeline: TimelineEvent[]
}

type Action = 'MarkReady' | 'MarkWiped' | 'ReportLost' | 'ReportStolen' | 'Recover' | 'AddNote'

/** Which actions are offered depends on where the asset is in its lifecycle. */
function availableActions(a: AssetDetails): { action: Action; label: string }[] {
  const next = new Set(a.nextStatuses)
  const actions: { action: Action; label: string }[] = []
  if (next.has('ReadyToDeploy')) actions.push({ action: 'MarkReady', label: 'Mark ready to deploy' })
  if (next.has('Wiped')) actions.push({ action: 'MarkWiped', label: 'Record wipe' })
  if (a.status === 'Lost' || a.status === 'Stolen') actions.push({ action: 'Recover', label: 'Recovered' })
  if (next.has('Lost')) actions.push({ action: 'ReportLost', label: 'Report lost' })
  if (next.has('Stolen')) actions.push({ action: 'ReportStolen', label: 'Report stolen' })
  actions.push({ action: 'AddNote', label: 'Add note' })
  return actions
}

const detailLabels: Record<string, string> = {
  Method: 'Method',
  Circumstances: 'Circumstances',
  ReportedBy: 'Reported by',
  PoliceReference: 'Police reference',
  WhereFound: 'Where found',
}

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')

export function AssetDetailsPage({ assetId, onBack }: { assetId: string; onBack: () => void }) {
  const [asset, setAsset] = useState<AssetDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [action, setAction] = useState<Action | null>(null)

  useEffect(() => {
    api<AssetDetails>(`/api/assets/${assetId}`)
      .then((a) => {
        setAsset(a)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [assetId, refresh])

  if (!asset) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>

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
                  {asset.assignedTo.displayName}
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
            <dt>Purchased</dt>
            <dd>{formatDate(asset.purchaseDate)}</dd>
            <dt>Warranty expires</dt>
            <dd>{formatDate(asset.warrantyExpiry)}</dd>
            <dt>Supplier</dt>
            <dd>{asset.supplier ?? '-'}</dd>
            <dt>Last seen in Intune</dt>
            <dd>{asset.isIntuneManaged ? formatDate(asset.lastSeenInIntune) : 'Not managed in Intune'}</dd>
          </dl>

          <h3>Actions</h3>
          <div className="action-buttons">
            {availableActions(asset).map((a) => (
              <button key={a.action} className={action === a.action ? 'primary' : ''} onClick={() => setAction(a.action)}>
                {a.label}
              </button>
            ))}
          </div>
          <p className="muted small">Assign, return, repair and retire arrive in the next steps.</p>

          {action && (
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
                  by {e.actor}
                  {e.ticketNumber && <> · ticket {e.ticketNumber}</>}
                </div>
                {e.details &&
                  Object.entries(e.details)
                    .filter(([, v]) => v)
                    .map(([k, v]) => (
                      <div key={k} className="small">
                        {detailLabels[k] ?? k}: {v}
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
