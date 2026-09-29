import { useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'
import { PriorityBadge, RequestStatusBadge } from '../RequestBadges'
import { requestReference, type RequestList } from '../requests'
import { useUser } from '../user'
import { NewRequestForm } from './NewRequestForm'

const views = [
  { key: 'All', label: 'All' },
  { key: 'AwaitingMe', label: 'Awaiting my approval' },
  { key: 'Mine', label: 'My requests' },
  { key: 'Pending', label: 'Pending approval' },
  { key: 'Approved', label: 'Approved' },
  { key: 'Ordered', label: 'Ordered' },
  { key: 'Received', label: 'Received' },
  { key: 'Completed', label: 'Completed' },
  { key: 'Closed', label: 'Rejected / cancelled' },
] as const

type ViewKey = (typeof views)[number]['key']

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')

/** Device requests (mockup 2). "Approvals" in the sidebar opens this on "Awaiting my approval". */
export function RequestsPage({ initialView = 'All', onChanged }: { initialView?: ViewKey; onChanged?: () => void }) {
  const nav = useNav()
  const { canRequest } = useUser()
  const [view, setView] = useState<ViewKey>(initialView)
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [list, setList] = useState<RequestList | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [refresh, setRefresh] = useState(0)

  // Search as you type, a moment after typing stops.
  useEffect(() => {
    const timer = setTimeout(() => setTerm(search.trim()), 250)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    let current = true
    api<RequestList>(`/api/requests?view=${view}${term ? `&search=${encodeURIComponent(term)}` : ''}`)
      .then((l) => {
        if (!current) return
        setList(l)
        setError(null)
      })
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [view, term, refresh])

  return (
    <>
      <PageHeader
        title="Device requests"
        subtitle="Requests for new and replacement devices, from approval to handover. Nothing is deleted; rejected and cancelled requests stay on record."
        actions={
          canRequest && !creating ? (
            <button className="primary" onClick={() => setCreating(true)}>
              <Plus size={16} /> New request
            </button>
          ) : undefined
        }
      />

      {creating && (
        <NewRequestForm
          onCancel={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false)
            setRefresh((n) => n + 1)
            onChanged?.()
            nav.openRequest(id)
          }}
        />
      )}

      <div className="tabs" role="tablist">
        {views.map((v) => (
          <button key={v.key} role="tab" aria-selected={view === v.key} className={view === v.key ? 'selected' : ''} onClick={() => setView(v.key)}>
            {v.label}
            {list && <span className="tab-count">{list.counts[v.key] ?? 0}</span>}
          </button>
        ))}
      </div>

      <div className="form-row filters">
        <input
          type="search"
          aria-label="Search requests"
          placeholder="Search REQ number, device, person, PO or ticket"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>

      {error && <p className="error">{error}</p>}
      {!list && !error && <p className="muted">Loading…</p>}
      {list && (
        <table>
          <thead>
            <tr>
              <th>Request</th>
              <th>Device</th>
              <th>For</th>
              <th>Department</th>
              <th>Requested by</th>
              <th>Status</th>
              <th>Priority</th>
              <th>Date</th>
            </tr>
          </thead>
          <tbody>
            {list.items.map((r) => (
              <tr key={r.id} className="clickable" onClick={() => nav.openRequest(r.id)}>
                <td>
                  <button className="link" onClick={() => nav.openRequest(r.id)}>
                    {requestReference(r.number)}
                  </button>
                </td>
                <td>
                  {r.deviceDescription}
                  <div className="muted small">{r.category}</div>
                </td>
                <td>{r.recipient}</td>
                <td>{r.department ?? '-'}</td>
                <td>{r.requestedByName}</td>
                <td>
                  <RequestStatusBadge status={r.status} />
                  {r.status === 'PendingApproval' && <div className="muted small">{r.approver ?? 'An administrator'}</div>}
                </td>
                <td>
                  <PriorityBadge priority={r.priority} />
                </td>
                <td>
                  {formatDate(r.submittedAt)}
                  {r.neededBy && <div className="muted small">needed {formatDate(r.neededBy)}</div>}
                </td>
              </tr>
            ))}
            {list.items.length === 0 && (
              <tr>
                <td colSpan={8} className="muted">
                  {view === 'AwaitingMe' ? 'Nothing is waiting for your approval.' : 'No requests here.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
      {list && list.total > list.items.length && (
        <p className="muted small">
          Showing the latest {list.items.length} of {list.total}. Search to narrow it down.
        </p>
      )}
    </>
  )
}
