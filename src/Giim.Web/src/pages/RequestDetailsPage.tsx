import { useEffect, useState, type ReactNode } from 'react'
import { api } from '../api'
import { LocationSelect } from '../LocationSelect'
import { useNav } from '../nav'
import { PriorityBadge, RequestStatusBadge } from '../RequestBadges'
import { reasonLabel, requestStatusLabel } from '../requests'
import { StatusBadge } from '../StatusBadge'
import { searchUrl, useSearch } from '../search'
import { AssignForm, type AssetOption, type AssignPayload } from './AssignReturnForms'

type RequestDetails = {
  id: string
  reference: string
  status: string
  priority: string
  reasonType: string
  reason: string
  deviceDescription: string
  specifications: string | null
  notes: string | null
  neededBy: string | null
  estimatedCost: number | null
  budgetCode: string | null
  ticketNumber: string | null
  submittedAt: string
  requestedBy: string
  requestedByName: string
  category: { id: string; name: string }
  department: string | null
  recipient: { id: string; displayName: string; jobTitle: string | null; status: string; email: string | null; manager: string | null }
  approver: { id: string; displayName: string; email: string | null } | null
  decision: { decidedByName: string; decidedAt: string; decisionComment: string | null } | null
  order: {
    supplier: string
    purchaseOrder: string
    orderCost: number | null
    orderedOn: string
    expectedDelivery: string | null
    trackingNumber: string | null
    purchaseNotes: string | null
  } | null
  asset: { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; status: string } | null
  receivedAt: string | null
  completedAt: string | null
  cancellationReason: string | null
  history: { id: number; occurredAt: string; type: string; toStatus: string; actorName: string; summary: string; comment: string | null }[]
  emails: { id: string; kind: string; toName: string | null; toAddress: string; status: string; createdAt: string; sentAt: string | null; attempts: number }[]
  can: { decide: boolean; answer: boolean; cancel: boolean; order: boolean; receive: boolean; fulfil: boolean; comment: boolean }
  decisionNote: string | null
}

type Action = 'approve' | 'reject' | 'ask' | 'answer' | 'cancel' | 'order' | 'receive' | 'fulfil'

const money = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' })
const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')
const formatDateTime = (value: string) => new Date(value).toLocaleString('en-AU', { dateStyle: 'medium', timeStyle: 'short' })
const today = () => new Date().toLocaleDateString('en-CA') // yyyy-mm-dd in local time

/** One device request (mockup 3): where it is, who decides, and everything that has happened to it. */
export function RequestDetailsPage({ requestId, onBack, onChanged }: { requestId: string; onBack: () => void; onChanged?: () => void }) {
  const nav = useNav()
  const [request, setRequest] = useState<RequestDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [action, setAction] = useState<Action | null>(null)
  const done = () => {
    setAction(null)
    setRefresh((n) => n + 1)
    onChanged?.()
  }

  useEffect(() => {
    api<RequestDetails>(`/api/requests/${requestId}`)
      .then((r) => {
        setRequest(r)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [requestId, refresh])

  if (!request) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  const r = request
  const post = (path: string, json: unknown) => api(`/api/requests/${r.id}/${path}`, { method: 'POST', json })

  const buttons: { key: Action; label: string; show: boolean; style?: string }[] = [
    { key: 'approve', label: 'Approve', show: r.can.decide, style: 'primary approve' },
    { key: 'reject', label: 'Reject', show: r.can.decide, style: 'danger' },
    { key: 'ask', label: 'Request info', show: r.can.decide },
    { key: 'answer', label: 'Answer', show: r.can.answer, style: 'primary' },
    { key: 'order', label: 'Record order', show: r.can.order, style: 'primary' },
    { key: 'receive', label: 'Receive device', show: r.can.receive, style: 'primary' },
    { key: 'fulfil', label: r.status === 'Received' ? 'Hand over' : 'Hand over from stock', show: r.can.fulfil, style: r.status === 'Received' ? 'primary' : undefined },
    { key: 'cancel', label: 'Cancel request', show: r.can.cancel },
  ]
  const lastQuestion = [...r.history].reverse().find((h) => h.type === 'InfoRequested')

  return (
    <>
      <button onClick={onBack}>← Back</button>
      <div className="details-header">
        <div>
          <h2>
            {r.reference} · {r.deviceDescription}
          </h2>
          <p className="muted">
            Requested {formatDate(r.submittedAt)} by {r.requestedByName} for{' '}
            <button className="link" onClick={() => nav.openPerson(r.recipient.id)}>
              {r.recipient.displayName}
            </button>
          </p>
        </div>
        <div className="header-actions">
          <RequestStatusBadge status={r.status} />
          <PriorityBadge priority={r.priority} />
        </div>
      </div>

      <div className="action-bar">
        {buttons
          .filter((b) => b.show)
          .map((b) => (
            <button key={b.key} className={b.style} onClick={() => setAction(action === b.key ? null : b.key)} aria-pressed={action === b.key}>
              {b.label}
            </button>
          ))}
        {r.decisionNote && <span className="muted small">{r.decisionNote}</span>}
      </div>

      {r.status === 'InfoRequested' && lastQuestion && (
        <p className="notice">
          <strong>{lastQuestion.actorName} asked:</strong> {lastQuestion.comment}
        </p>
      )}

      {action && (
        <section className="panel">
          {action === 'approve' && (
            <TextActionForm
              title="Approve"
              label="Comment (optional)"
              optional
              extra={(value, set) => (
                <label>
                  Budget / cost centre
                  <input value={value} onChange={(e) => set(e.target.value)} placeholder="e.g. SALES-2026" />
                </label>
              )}
              extraInitial={r.budgetCode ?? ''}
              submitLabel="Approve"
              onSubmit={(comment, budgetCode) => post('decision', { decision: 'Approve', comment, budgetCode, expectedStatus: r.status })}
              onDone={done}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'reject' && (
            <TextActionForm
              title="Reject"
              label="Reason (the requester sees this)"
              placeholder="e.g. Existing laptop available in inventory"
              submitLabel="Reject"
              onSubmit={(comment) => post('decision', { decision: 'Reject', comment, expectedStatus: r.status })}
              onDone={done}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'ask' && (
            <TextActionForm
              title="Request more information"
              label="Question for the requester"
              placeholder="e.g. Is there a spare laptop in stock?"
              submitLabel="Send question"
              onSubmit={(comment) => post('decision', { decision: 'AskForInfo', comment, expectedStatus: r.status })}
              onDone={done}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'answer' && (
            <TextActionForm
              title="Answer the approver"
              label={lastQuestion?.comment ?? 'Answer'}
              submitLabel="Send answer"
              onSubmit={(text) => post('answer', { text, expectedStatus: r.status })}
              onDone={done}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'cancel' && (
            <TextActionForm
              title="Cancel the request"
              label="Reason"
              placeholder="e.g. Starter withdrew"
              submitLabel="Cancel request"
              onSubmit={(text) => post('cancel', { text, expectedStatus: r.status })}
              onDone={done}
              onCancel={() => setAction(null)}
            />
          )}
          {action === 'order' && <OrderForm request={r} onSubmit={(json) => post('order', json)} onDone={done} onCancel={() => setAction(null)} />}
          {action === 'receive' && <ReceiveForm request={r} onSubmit={(json) => post('receive', json)} onDone={done} onCancel={() => setAction(null)} />}
          {action === 'fulfil' && (
            <HandOverForm request={r} onSubmit={(json) => post('fulfil', json)} onDone={done} onCancel={() => setAction(null)} />
          )}
        </section>
      )}

      <Stepper request={r} />

      <div className="details-grid">
        <section className="panel">
          <h3>Request</h3>
          <dl className="facts">
            <dt>For</dt>
            <dd>
              <button className="link" onClick={() => nav.openPerson(r.recipient.id)}>
                {r.recipient.displayName}
              </button>
              <div className="muted small">
                {[r.recipient.jobTitle, r.recipient.email].filter(Boolean).join(' · ')}
                {r.recipient.status !== 'Active' && ` · ${r.recipient.status}`}
              </div>
            </dd>
            <dt>Department</dt>
            <dd>{r.department ?? '-'}</dd>
            <dt>Manager</dt>
            <dd>{r.recipient.manager ?? '-'}</dd>
            <dt>Requested by</dt>
            <dd>{r.requestedByName}</dd>
            <dt>Device</dt>
            <dd>
              {r.deviceDescription}
              <div className="muted small">{r.category.name}</div>
            </dd>
            <dt>Specifications</dt>
            <dd>{r.specifications ?? '-'}</dd>
            <dt>Reason</dt>
            <dd>
              {reasonLabel[r.reasonType] ?? r.reasonType}: {r.reason}
            </dd>
            <dt>Needed by</dt>
            <dd>{formatDate(r.neededBy)}</dd>
            <dt>Estimated cost</dt>
            <dd>{r.estimatedCost != null ? money.format(r.estimatedCost) : '-'}</dd>
            <dt>Ticket</dt>
            <dd>
              {r.ticketNumber ? (
                <button className="link" onClick={() => nav.openTicket(r.ticketNumber!)}>
                  {r.ticketNumber}
                </button>
              ) : (
                '-'
              )}
            </dd>
            {r.notes && (
              <>
                <dt>Notes</dt>
                <dd>{r.notes}</dd>
              </>
            )}
          </dl>
        </section>

        <div>
          <section className="panel" style={{ marginTop: 0 }}>
            <h3>Approval</h3>
            <dl className="facts">
              <dt>Status</dt>
              <dd>
                <RequestStatusBadge status={r.status} />
              </dd>
              <dt>Approver</dt>
              <dd>{r.approver?.displayName ?? 'An administrator (no manager on record)'}</dd>
              <dt>{r.status === 'Rejected' ? 'Rejected by' : 'Decided by'}</dt>
              <dd>{r.decision?.decidedByName ?? '-'}</dd>
              <dt>Date</dt>
              <dd>{r.decision ? formatDateTime(r.decision.decidedAt) : '-'}</dd>
              <dt>{r.status === 'Rejected' ? 'Reason' : 'Comments'}</dt>
              <dd>{r.decision?.decisionComment ?? '-'}</dd>
              <dt>Budget code</dt>
              <dd>{r.budgetCode ?? '-'}</dd>
              {r.cancellationReason && (
                <>
                  <dt>Cancelled</dt>
                  <dd>{r.cancellationReason}</dd>
                </>
              )}
            </dl>
          </section>

          {(r.order || r.asset) && (
            <section className="panel">
              <h3>Purchasing and device</h3>
              <dl className="facts">
                {r.order && (
                  <>
                    <dt>Supplier</dt>
                    <dd>{r.order.supplier}</dd>
                    <dt>PO number</dt>
                    <dd>{r.order.purchaseOrder}</dd>
                    <dt>Cost</dt>
                    <dd>{r.order.orderCost != null ? money.format(r.order.orderCost) : '-'}</dd>
                    <dt>Ordered</dt>
                    <dd>{formatDate(r.order.orderedOn)}</dd>
                    <dt>Expected</dt>
                    <dd>{formatDate(r.order.expectedDelivery)}</dd>
                    {r.order.trackingNumber && (
                      <>
                        <dt>Tracking</dt>
                        <dd>{r.order.trackingNumber}</dd>
                      </>
                    )}
                    {r.order.purchaseNotes && (
                      <>
                        <dt>Notes</dt>
                        <dd>{r.order.purchaseNotes}</dd>
                      </>
                    )}
                  </>
                )}
                {r.asset && (
                  <>
                    <dt>Device</dt>
                    <dd>
                      <button className="link" onClick={() => nav.openAsset(r.asset!.id)}>
                        {r.asset.manufacturer} {r.asset.model}
                      </button>{' '}
                      <StatusBadge status={r.asset.status} />
                      <div className="muted small">
                        {r.asset.assetTag ?? 'No asset tag'} · S/N {r.asset.serialNumber}
                      </div>
                    </dd>
                  </>
                )}
              </dl>
            </section>
          )}
        </div>
      </div>

      <div className="details-grid">
        <section className="panel">
          <h3>History</h3>
          <ol className="timeline">
            {[...r.history].reverse().map((h) => (
              <li key={h.id}>
                <div>
                  <strong>{h.summary}</strong> <span className="muted small">by {h.actorName}</span>
                </div>
                <div className="timeline-when">{formatDateTime(h.occurredAt)}</div>
                {h.comment && <div className="timeline-note">{h.comment}</div>}
              </li>
            ))}
          </ol>
          {r.can.comment && <CommentBox onSubmit={(text) => post('comment', { text })} onDone={done} />}
        </section>

        <section className="panel">
          <h3>Emails</h3>
          {r.emails.length === 0 ? (
            <p className="muted small">No emails sent for this request yet.</p>
          ) : (
            <ul className="plain-list">
              {r.emails.map((e) => (
                <li key={e.id}>
                  {emailLabel[e.kind] ?? e.kind} → {e.toName ?? e.toAddress}
                  <div className="muted small">
                    {e.status === 'Sent'
                      ? `Sent ${formatDateTime(e.sentAt!)}`
                      : e.status === 'Failed'
                        ? `Not sent after ${e.attempts} attempts`
                        : e.attempts > 0
                          ? `Retrying (attempt ${e.attempts} failed)`
                          : 'Queued'}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </>
  )
}

const emailLabel: Record<string, string> = {
  RequestApprovalNeeded: 'Approval needed',
  RequestInfoProvided: 'Information provided',
  RequestInfoRequested: 'More information needed',
  RequestApproved: 'Approved',
  RequestRejected: 'Rejected',
  RequestCompleted: 'Handed over',
}

/** Requested → Pending approval → Approved → Ordered → Received → Completed, as in mockup 3. */
function Stepper({ request: r }: { request: RequestDetails }) {
  const reached = new Set(r.history.map((h) => h.toStatus))
  const fromStock = r.status === 'Completed' && !reached.has('Ordered')
  const closed = r.status === 'Rejected' || r.status === 'Cancelled'
  const steps = [
    { key: 'Submitted', label: 'Requested', done: true, when: r.submittedAt },
    { key: 'PendingApproval', label: 'Pending approval', done: reached.has('Approved') || r.status === 'Rejected' },
    { key: 'Approved', label: r.status === 'Rejected' ? 'Rejected' : 'Approved', done: reached.has('Approved'), when: r.decision?.decidedAt },
    { key: 'Ordered', label: fromStock ? 'From stock' : 'Ordered', done: reached.has('Ordered') || fromStock, when: r.order?.orderedOn },
    { key: 'Received', label: fromStock ? '—' : 'Received', done: reached.has('Received') || fromStock, when: r.receivedAt },
    { key: 'Completed', label: 'Handed over', done: r.status === 'Completed', when: r.completedAt },
  ]
  const currentIndex = closed ? -1 : steps.findIndex((s) => !s.done)
  return (
    <ol className="stepper" aria-label="Progress">
      {steps.map((s, i) => (
        <li
          key={s.key}
          className={[
            s.done ? 'done' : '',
            i === currentIndex ? 'current' : '',
            r.status === 'Rejected' && s.key === 'Approved' ? 'failed' : '',
          ].join(' ')}
        >
          <span className="step-dot">{i + 1}</span>
          <span className="step-label">{s.label}</span>
          <span className="step-when">
            {i === currentIndex ? (r.status === 'InfoRequested' ? requestStatusLabel[r.status] : 'Current') : s.when ? formatDate(s.when) : ''}
          </span>
        </li>
      ))}
      {r.status === 'Cancelled' && (
        <li className="failed">
          <span className="step-dot">✕</span>
          <span className="step-label">Cancelled</span>
        </li>
      )}
    </ol>
  )
}

function useSubmit(onDone: () => void) {
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const run = async (work: () => Promise<unknown>) => {
    setSaving(true)
    setError(null)
    try {
      await work()
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }
  return { error, saving, run }
}

function TextActionForm({
  title,
  label,
  placeholder,
  optional = false,
  submitLabel,
  extra,
  extraInitial = '',
  onSubmit,
  onDone,
  onCancel,
}: {
  title: string
  label: string
  placeholder?: string
  optional?: boolean
  submitLabel: string
  extra?: (value: string, set: (v: string) => void) => ReactNode
  extraInitial?: string
  onSubmit: (text: string, extra: string) => Promise<unknown>
  onDone: () => void
  onCancel: () => void
}) {
  const [text, setText] = useState('')
  const [extraValue, setExtraValue] = useState(extraInitial)
  const { error, saving, run } = useSubmit(onDone)
  return (
    <div>
      <h3>{title}</h3>
      <div className="form-row">
        <label className="grow">
          {label}
          <textarea rows={2} value={text} onChange={(e) => setText(e.target.value)} placeholder={placeholder} autoFocus />
        </label>
        {extra?.(extraValue, setExtraValue)}
      </div>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={saving || (!optional && !text.trim())} onClick={() => run(() => onSubmit(text, extraValue))}>
        {saving ? 'Saving…' : submitLabel}
      </button>{' '}
      <button onClick={onCancel}>Back</button>
    </div>
  )
}

function OrderForm({ request, onSubmit, onDone, onCancel }: FormProps) {
  const [f, setF] = useState({
    supplier: '',
    purchaseOrder: '',
    cost: request.estimatedCost != null ? String(request.estimatedCost) : '',
    orderedOn: today(),
    expectedDelivery: '',
    trackingNumber: '',
    notes: '',
  })
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })
  const { error, saving, run } = useSubmit(onDone)
  return (
    <div>
      <h3>Record the order</h3>
      <div className="form-row">
        <label>
          Supplier
          <input value={f.supplier} onChange={set('supplier')} placeholder="e.g. Dell" autoFocus />
        </label>
        <label>
          PO number
          <input value={f.purchaseOrder} onChange={set('purchaseOrder')} placeholder="e.g. PO33445" />
        </label>
        <label>
          Cost ($)
          <input type="number" min={0} step="0.01" value={f.cost} onChange={set('cost')} style={{ width: 120 }} />
        </label>
        <label>
          Ordered
          <input type="date" value={f.orderedOn} onChange={set('orderedOn')} />
        </label>
        <label>
          Expected delivery
          <input type="date" value={f.expectedDelivery} onChange={set('expectedDelivery')} />
        </label>
      </div>
      <div className="form-row">
        <label>
          Tracking number
          <input value={f.trackingNumber} onChange={set('trackingNumber')} />
        </label>
        <label className="grow">
          Notes
          <input value={f.notes} onChange={set('notes')} />
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <button
        className="primary"
        disabled={saving || !f.supplier.trim() || !f.purchaseOrder.trim()}
        onClick={() =>
          run(() =>
            onSubmit({
              supplier: f.supplier,
              purchaseOrder: f.purchaseOrder,
              cost: f.cost ? Number(f.cost) : null,
              orderedOn: f.orderedOn || null,
              expectedDelivery: f.expectedDelivery || null,
              trackingNumber: f.trackingNumber || null,
              notes: f.notes || null,
              expectedStatus: request.status,
            }),
          )
        }
      >
        {saving ? 'Saving…' : 'Record order'}
      </button>{' '}
      <button onClick={onCancel}>Back</button>
    </div>
  )
}

function ReceiveForm({ request, onSubmit, onDone, onCancel }: FormProps) {
  const [f, setF] = useState({ serialNumber: '', assetTag: '', manufacturer: request.order?.supplier ?? '', model: '', warrantyExpiry: '', notes: '' })
  const [locationId, setLocationId] = useState('')
  const [readyToIssue, setReadyToIssue] = useState(true)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })
  const { error, saving, run } = useSubmit(onDone)
  return (
    <div>
      <h3>Receive the device</h3>
      <p className="muted small">
        Creates the asset record for {request.deviceDescription}, with the supplier, PO and cost from the order.
      </p>
      <div className="form-row">
        <label>
          Serial number
          <input value={f.serialNumber} onChange={set('serialNumber')} placeholder="Scan or type" autoFocus />
        </label>
        <label>
          Asset tag
          <input value={f.assetTag} onChange={set('assetTag')} placeholder="optional" />
        </label>
        <label>
          Manufacturer
          <input value={f.manufacturer} onChange={set('manufacturer')} />
        </label>
        <label className="grow">
          Model
          <input value={f.model} onChange={set('model')} placeholder={request.deviceDescription} />
        </label>
      </div>
      <div className="form-row">
        <label>
          Warranty ends
          <input type="date" value={f.warrantyExpiry} onChange={set('warrantyExpiry')} />
        </label>
        <label>
          Location
          <LocationSelect value={locationId} onChange={setLocationId} allowNone />
        </label>
        <label className="grow">
          Notes
          <input value={f.notes} onChange={set('notes')} placeholder="e.g. Box slightly damaged, device fine" />
        </label>
      </div>
      <label className="inline">
        <input type="checkbox" checked={readyToIssue} onChange={(e) => setReadyToIssue(e.target.checked)} /> Ready to hand over now (untick if
        it still needs setting up)
      </label>
      {error && <p className="error">{error}</p>}
      <button
        className="primary"
        disabled={saving || !f.serialNumber.trim() || !f.manufacturer.trim() || !f.model.trim()}
        onClick={() =>
          run(() =>
            onSubmit({
              serialNumber: f.serialNumber,
              assetTag: f.assetTag || null,
              manufacturer: f.manufacturer,
              model: f.model,
              warrantyExpiry: f.warrantyExpiry || null,
              notes: f.notes || null,
              locationId: locationId || null,
              readyToIssue,
              expectedStatus: request.status,
            }),
          )
        }
      >
        {saving ? 'Saving…' : 'Receive'}
      </button>{' '}
      <button onClick={onCancel}>Back</button>
    </div>
  )
}

/**
 * Hand over: the device received for this request, or (when approved) one chosen from stock. Uses the normal assign
 * form, so accessories are issued the same way, and the request completes in the same step.
 */
function HandOverForm({ request, onSubmit, onDone, onCancel }: FormProps) {
  const received = request.asset
  const [assetTerm, setAssetTerm] = useState('')
  const [chosen, setChosen] = useState<AssetOption | null>(null)
  const candidates = useSearch<AssetOption>(
    searchUrl(assetTerm, `/api/assets?status=ReadyToDeploy&categoryId=${request.category.id}&pageSize=8&search=`),
  )

  const asset = received ?? chosen
  if (!asset)
    return (
      <div>
        <h3>Hand over from stock</h3>
        <label className="stacked">
          {request.category.name} ready to deploy
          <input value={assetTerm} onChange={(e) => setAssetTerm(e.target.value)} placeholder="Search tag, serial or model" autoFocus />
          {candidates.length > 0 && (
            <ul className="picker">
              {candidates.map((a) => (
                <li key={a.id}>
                  <button onClick={() => setChosen(a)}>
                    {a.assetTag ?? a.serialNumber} {a.manufacturer} {a.model}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </label>
        <button onClick={onCancel}>Back</button>
      </div>
    )

  if (received && received.status !== 'ReadyToDeploy')
    return (
      <div>
        <h3>Hand over</h3>
        <p>
          {received.manufacturer} {received.model} is {received.status === 'Received' ? 'still being set up' : received.status}. Mark it ready to
          deploy on its asset page first.
        </p>
        <button onClick={onCancel}>Back</button>
      </div>
    )

  return (
    <AssignForm
      title={`Hand over ${asset.manufacturer} ${asset.model} (${asset.assetTag ?? asset.serialNumber})`}
      assetId={asset.id}
      expectedStatus="ReadyToDeploy"
      fixedPerson={{
        id: request.recipient.id,
        displayName: request.recipient.displayName,
        userPrincipalName: request.recipient.email,
        department: request.department,
        status: request.recipient.status,
      }}
      onSubmit={(payload: AssignPayload) =>
        onSubmit({
          assetId: asset.id,
          expectedAssetStatus: payload.expectedStatus,
          locationId: payload.locationId,
          ticketNumber: payload.ticketNumber,
          accessories: payload.accessories,
          expectedStatus: request.status,
        })
      }
      onDone={onDone}
      onCancel={onCancel}
    />
  )
}

function CommentBox({ onSubmit, onDone }: { onSubmit: (text: string) => Promise<unknown>; onDone: () => void }) {
  const [text, setText] = useState('')
  const { error, saving, run } = useSubmit(() => {
    setText('')
    onDone()
  })
  return (
    <div className="form-row">
      <label className="grow">
        Add a comment
        <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Visible to everyone who can see this request" />
      </label>
      <button disabled={saving || !text.trim()} onClick={() => run(() => onSubmit(text))}>
        Add
      </button>
      {error && <p className="error">{error}</p>}
    </div>
  )
}

type FormProps = {
  request: RequestDetails
  onSubmit: (json: unknown) => Promise<unknown>
  onDone: () => void
  onCancel: () => void
}
