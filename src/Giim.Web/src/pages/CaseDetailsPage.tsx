import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { caseStatusLabel, caseTypeLabel, relativeDay } from '../cases'
import { useNav } from '../nav'
import { RequestStatusBadge } from '../RequestBadges'
import { requestReference } from '../requests'
import { StatusBadge } from '../StatusBadge'
import { useUser } from '../user'
import { AutomationPanel } from './AutomationPanel'
import { Progress } from './CasesPage'

type Task = {
  id: string
  order: number
  title: string
  kind: 'Manual' | 'Automated'
  status: string
  requiresApproval: boolean
  approvedBy: string | null
  approvedAt: string | null
  source: string
  sourceId: string | null
  categoryId: string | null
  completedBy: string | null
  completedAt: string | null
  notes: string | null
  /** The automation step that can do it (on-prem agent or GIIM), or null. */
  step: string | null
  request: { id: string; number: number; status: string } | null
  asset: { id: string; assetTag: string | null; serialNumber: string; status: string; stillHeld: boolean } | null
}

type CaseDetails = {
  id: string
  type: 'Onboarding' | 'Offboarding'
  status: string
  dueDate: string | null
  ticketNumber: string | null
  notes: string | null
  createdAt: string
  createdBy: string | null
  completedAt: string | null
  cancellationReason: string | null
  cancelledBy: string | null
  profile: string | null
  person: {
    id: string
    displayName: string
    employeeId: string
    jobTitle: string | null
    status: string
    track: string
    email: string | null
    department: string | null
    manager: string | null
  }
  tasks: Task[]
  can: { approve: boolean }
}

type Category = { id: string; name: string; isActive: boolean }

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')
const formatDateTime = (value: string) => new Date(value).toLocaleString('en-AU', { dateStyle: 'medium', timeStyle: 'short' })
const finished = (t: Task) => t.status === 'Done' || t.status === 'Skipped'

/** One starter or leaver checklist: tick tasks off, raise device requests, approve destructive steps. */
export function CaseDetailsPage({ caseId, onBack }: { caseId: string; onBack: () => void }) {
  const nav = useNav()
  const { canChange } = useUser()
  const [details, setDetails] = useState<CaseDetails | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [open, setOpen] = useState<{ taskId: string; mode: 'complete' | 'skip' | 'request' } | null>(null)
  const [text, setText] = useState('')
  const [categories, setCategories] = useState<Category[]>([])
  const [categoryId, setCategoryId] = useState('')
  const [newTask, setNewTask] = useState('')
  const [cancelling, setCancelling] = useState(false)
  const [busy, setBusy] = useState(false)
  const refreshCase = useCallback(() => setRefresh((n) => n + 1), [])

  useEffect(() => {
    api<CaseDetails>(`/api/cases/${caseId}`)
      .then((d) => {
        setDetails(d)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [caseId, refresh])

  useEffect(() => {
    api<Category[]>('/api/categories')
      .then((c) => setCategories(c.filter((x) => x.isActive)))
      .catch(() => setCategories([]))
  }, [])

  if (!details) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  const c = details
  const closed = c.status === 'Completed' || c.status === 'Cancelled'
  const editable = canChange && c.status !== 'Cancelled'

  const run = async (path: string, json: unknown = {}) => {
    setBusy(true)
    setActionError(null)
    try {
      await api(`/api/cases/${c.id}/${path}`, { method: 'POST', json })
      setOpen(null)
      setText('')
      setRefresh((n) => n + 1)
      return true
    } catch (e) {
      setActionError((e as Error).message)
      return false
    } finally {
      setBusy(false)
    }
  }

  const done = c.tasks.filter(finished).length
  const waitingForApproval = c.tasks.filter((t) => t.requiresApproval && !t.approvedBy && !finished(t))

  return (
    <>
      <button onClick={onBack}>← Back</button>
      <div className="details-header">
        <div>
          <h2>
            {caseTypeLabel[c.type]}: {c.person.displayName}
          </h2>
          <p className="muted">
            {c.type === 'Onboarding' ? 'Starts' : 'Last day'} {formatDate(c.dueDate)} ({relativeDay(c.dueDate)}) · {c.person.department ?? 'No department'}
            {c.ticketNumber && (
              <>
                {' '}
                · ticket{' '}
                <button className="link" onClick={() => nav.openTicket(c.ticketNumber!)}>
                  {c.ticketNumber}
                </button>
              </>
            )}
          </p>
        </div>
        <div className="header-actions">
          <Progress done={done} total={c.tasks.length} />
          <span className={`status case-${c.status}`}>{caseStatusLabel[c.status] ?? c.status}</span>
        </div>
      </div>

      {c.status === 'Cancelled' && (
        <p className="notice">
          Cancelled by {c.cancelledBy}: {c.cancellationReason}
        </p>
      )}
      {waitingForApproval.length > 0 && !closed && (
        <p className="notice">
          {waitingForApproval.length} step{waitingForApproval.length === 1 ? '' : 's'} need{waitingForApproval.length === 1 ? 's' : ''} an
          administrator&apos;s approval before anyone carries {waitingForApproval.length === 1 ? 'it' : 'them'} out.
        </p>
      )}
      {actionError && <p className="error">{actionError}</p>}

      <div className="details-grid">
        <section className="panel">
          <h3>Checklist</h3>
          <ol className="checklist">
            {c.tasks.map((t) => (
              <li key={t.id} className={`task task-${t.status}`}>
                <span className="task-mark" aria-hidden="true">
                  {t.status === 'Done'
                    ? '✓'
                    : t.status === 'Skipped'
                      ? '–'
                      : t.status === 'Waiting' || t.status === 'Running'
                        ? '…'
                        : t.status === 'Failed'
                          ? '!'
                          : ''}
                </span>
                <div className="task-body">
                  <div>
                    <span className={finished(t) ? 'task-title finished' : 'task-title'}>{t.title}</span>{' '}
                    {t.kind === 'Automated' && !finished(t) && <AutomationTag task={t} />}
                    {t.requiresApproval && (
                      <span className={`tag ${t.approvedBy ? 'tag-ok' : 'tag-warn'}`}>{t.approvedBy ? `approved by ${t.approvedBy}` : 'needs approval'}</span>
                    )}
                  </div>

                  {t.request && (
                    <div className="small">
                      <button className="link" onClick={() => nav.openRequest(t.request!.id)}>
                        {requestReference(t.request.number)}
                      </button>{' '}
                      <RequestStatusBadge status={t.request.status} />
                    </div>
                  )}
                  {t.asset && (
                    <div className="small">
                      <button className="link" onClick={() => nav.openAsset(t.asset!.id)}>
                        {t.asset.assetTag ?? t.asset.serialNumber}
                      </button>{' '}
                      <StatusBadge status={t.asset.status} />{' '}
                      {t.asset.stillHeld && !finished(t) && <span className="muted">still with them; ticks off when returned on the asset page</span>}
                    </div>
                  )}
                  {(t.completedBy || t.notes) && (
                    <div className="muted small task-notes">
                      {t.completedBy && t.completedAt && (
                        <div>
                          {t.status === 'Skipped' ? 'Skipped' : 'Done'} by {t.completedBy}, {formatDateTime(t.completedAt)}
                        </div>
                      )}
                      {t.notes?.split('\n').map((line, i) => <div key={i}>{line}</div>)}
                    </div>
                  )}

                  {editable && open?.taskId === t.id && open.mode !== 'request' && (
                    <div className="form-row">
                      <label className="grow">
                        {open.mode === 'complete' ? 'Note (optional)' : 'Why isn’t it needed?'}
                        <input value={text} onChange={(e) => setText(e.target.value)} autoFocus />
                      </label>
                      <button
                        className="primary"
                        disabled={busy || (open.mode === 'skip' && !text.trim())}
                        onClick={() => run(`tasks/${t.id}/${open.mode}`, { text })}
                      >
                        {open.mode === 'complete' ? 'Mark done' : 'Skip'}
                      </button>
                      <button onClick={() => setOpen(null)}>Back</button>
                    </div>
                  )}
                  {editable && open?.taskId === t.id && open.mode === 'request' && (
                    <div className="form-row">
                      {!t.categoryId && (
                        <label>
                          Device type
                          <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                            <option value="">Choose…</option>
                            {categories.map((x) => (
                              <option key={x.id} value={x.id}>
                                {x.name}
                              </option>
                            ))}
                          </select>
                        </label>
                      )}
                      <label className="grow">
                        Device
                        <input value={text} onChange={(e) => setText(e.target.value)} placeholder={t.title.replace('Allocate and scan: ', '')} />
                      </label>
                      <button
                        className="primary"
                        disabled={busy || (!t.categoryId && !categoryId)}
                        onClick={() => run(`tasks/${t.id}/device-request`, { categoryId: categoryId || null, deviceDescription: text || null })}
                      >
                        Raise request
                      </button>
                      <button onClick={() => setOpen(null)}>Back</button>
                    </div>
                  )}

                  {editable && open?.taskId !== t.id && (
                    <div className="task-actions">
                      {!finished(t) && (
                        <>
                          {(!t.requiresApproval || t.approvedBy) && (
                            <button className="link" onClick={() => setOpen({ taskId: t.id, mode: 'complete' })}>
                              Mark done
                            </button>
                          )}
                          {t.requiresApproval && !t.approvedBy && c.can.approve && (
                            <button className="link" disabled={busy} onClick={() => run(`tasks/${t.id}/approve`)}>
                              Approve
                            </button>
                          )}
                          {c.type === 'Onboarding' && t.source === 'ProfileItem' && t.title.startsWith('Allocate and scan') && !t.request && (
                            <button className="link" onClick={() => setOpen({ taskId: t.id, mode: 'request' })}>
                              Raise device request
                            </button>
                          )}
                          <button className="link" onClick={() => setOpen({ taskId: t.id, mode: 'skip' })}>
                            Skip
                          </button>
                        </>
                      )}
                      {finished(t) && (
                        <button className="link" disabled={busy} onClick={() => run(`tasks/${t.id}/reopen`)}>
                          Reopen
                        </button>
                      )}
                    </div>
                  )}
                </div>
              </li>
            ))}
          </ol>

          {editable && (
            <div className="form-row">
              <label className="grow">
                Add a task
                <input value={newTask} onChange={(e) => setNewTask(e.target.value)} placeholder="e.g. Collect building pass" />
              </label>
              <button
                disabled={busy || !newTask.trim()}
                onClick={async () => {
                  if (await run('tasks', { text: newTask })) setNewTask('')
                }}
              >
                Add
              </button>
            </div>
          )}
        </section>

        <div>
          {c.type === 'Onboarding' && <AutomationPanel caseId={c.id} refreshKey={refresh} onChanged={refreshCase} />}
          <section className="panel" style={c.type === 'Onboarding' ? undefined : { marginTop: 0 }}>
            <h3>{c.type === 'Onboarding' ? 'Starter' : 'Leaver'}</h3>
            <dl className="facts">
              <dt>Name</dt>
              <dd>
                <button className="link" onClick={() => nav.openPerson(c.person.id)}>
                  {c.person.displayName}
                </button>
                <div className="muted small">
                  {c.person.employeeId}
                  {c.person.email ? ` · ${c.person.email}` : ' · no account yet'}
                </div>
              </dd>
              <dt>Job title</dt>
              <dd>{c.person.jobTitle ?? '-'}</dd>
              <dt>Department</dt>
              <dd>{c.person.department ?? '-'}</dd>
              <dt>Manager</dt>
              <dd>{c.person.manager ?? '-'}</dd>
              <dt>Track</dt>
              <dd>{c.person.track}</dd>
              {c.profile && (
                <>
                  <dt>Profile</dt>
                  <dd>{c.profile}</dd>
                </>
              )}
              <dt>Created</dt>
              <dd>
                {formatDateTime(c.createdAt)}
                {c.createdBy && <div className="muted small">by {c.createdBy}</div>}
              </dd>
              {c.completedAt && (
                <>
                  <dt>Completed</dt>
                  <dd>{formatDateTime(c.completedAt)}</dd>
                </>
              )}
              {c.notes && (
                <>
                  <dt>Notes</dt>
                  <dd>{c.notes}</dd>
                </>
              )}
            </dl>
          </section>

          {editable && !closed && (
            <section className="panel">
              {cancelling ? (
                <div className="form-row">
                  <label className="grow">
                    Why is it being cancelled?
                    <input value={text} onChange={(e) => setText(e.target.value)} placeholder="e.g. Offer withdrawn" autoFocus />
                  </label>
                  <button className="danger" disabled={busy || !text.trim()} onClick={async () => setCancelling(!(await run('cancel', { text })))}>
                    Cancel checklist
                  </button>
                  <button onClick={() => setCancelling(false)}>Back</button>
                </div>
              ) : (
                <button onClick={() => setCancelling(true)}>Cancel this checklist…</button>
              )}
            </section>
          )}
        </div>
      </div>
    </>
  )
}

/** What automation means for a task: can run automatically, running, failed, or (leavers, for now) done by hand. */
function AutomationTag({ task: t }: { task: Task }) {
  if (!t.step)
    return (
      <span className="tag" title="A later phase will do this automatically; until then, do it by hand and tick it off.">
        automated later
      </span>
    )
  if (t.status === 'Failed') return <span className="tag tag-warn">automation failed</span>
  if (t.status === 'Running') return <span className="tag">running automatically</span>
  if (t.status === 'Waiting') return <span className="tag">waiting for the start date</span>
  return (
    <span className="tag" title="The on-prem agent or GIIM can do this: see Automation. You can still do it by hand and tick it off.">
      can run automatically
    </span>
  )
}
