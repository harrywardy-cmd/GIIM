import { useEffect, useState } from 'react'
import { api } from '../api'
import { PageHeader } from '../PageHeader'
import { LegacyOwnersPanel } from './LegacyOwnersPanel'
import { PersonProfile } from './PersonProfile'

type PersonRow = {
  id: string
  employeeId: string
  displayName: string
  userPrincipalName: string | null
  department: string | null
  jobTitle: string | null
  location: string | null
  status: string
  assetCount: number
}

const PAGE_SIZE = 50

export function PeoplePage() {
  const [tab, setTab] = useState<'people' | 'owners'>('people')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<{ total: number; people: PersonRow[] } | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [syncing, setSyncing] = useState(false)

  useEffect(() => {
    if (openId || tab !== 'people') return
    const controller = new AbortController()
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (search.trim()) params.set('search', search.trim())
    if (status) params.set('status', status)
    const timer = setTimeout(() => {
      api<{ total: number; people: PersonRow[] }>(`/api/people?${params}`, { signal: controller.signal })
        .then((d) => {
          setData(d)
          setError(null)
        })
        .catch((e: Error) => {
          if (e.name !== 'AbortError') setError(e.message)
        })
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [search, status, page, openId, tab, refresh])

  const sync = async () => {
    setSyncing(true)
    setError(null)
    try {
      const run = await api<{ devicesSeen: number; added: number; updated: number }>('/api/people/sync', { method: 'POST' })
      setMessage(`Directory synced: ${run.devicesSeen.toLocaleString()} people (${run.added} new, ${run.updated} updated).`)
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSyncing(false)
    }
  }

  if (openId) return <PersonProfile personId={openId} onBack={() => setOpenId(null)} onOpenPerson={setOpenId} />

  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1

  return (
    <>
      <PageHeader
        title="People"
        subtitle="Staff come from the directory (sample file now, Active Directory later)."
        actions={
          <button onClick={sync} disabled={syncing}>
            {syncing ? 'Syncing…' : 'Sync directory'}
          </button>
        }
      />
      {message && <p className="success">{message}</p>}
      {error && <p className="error">{error}</p>}

      <div className="tabs">
        <button className={tab === 'people' ? 'selected' : ''} onClick={() => setTab('people')}>
          All people
        </button>
        <button className={tab === 'owners' ? 'selected' : ''} onClick={() => setTab('owners')}>
          Link spreadsheet owners
        </button>
      </div>

      {tab === 'owners' ? (
        <LegacyOwnersPanel onOpenPerson={setOpenId} />
      ) : (
        <>
          <div className="form-row">
            <input
              type="search"
              placeholder="Search name, employee ID or email"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value)
                setPage(1)
              }}
            />
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value)
                setPage(1)
              }}
            >
              <option value="">All statuses</option>
              <option value="Active">Active</option>
              <option value="Pending">Starting soon</option>
              <option value="Leaving">Leaving</option>
              <option value="Left">Left</option>
            </select>
          </div>

          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Department</th>
                <th>Job title</th>
                <th>Location</th>
                <th>Status</th>
                <th>Assets</th>
              </tr>
            </thead>
            <tbody>
              {data?.people.map((p) => (
                <tr key={p.id} className="clickable" onClick={() => setOpenId(p.id)}>
                  <td>
                    <button className="link" onClick={() => setOpenId(p.id)}>
                      {p.displayName}
                    </button>
                    <div className="muted small">
                      {p.employeeId} · {p.userPrincipalName}
                    </div>
                  </td>
                  <td>{p.department ?? '-'}</td>
                  <td>{p.jobTitle ?? '-'}</td>
                  <td>{p.location ?? '-'}</td>
                  <td>
                    <span className={`status person-${p.status}`}>{p.status}</span>
                  </td>
                  <td>{p.assetCount}</td>
                </tr>
              ))}
              {data && data.people.length === 0 && (
                <tr>
                  <td colSpan={6} className="muted">
                    No people found. Use “Sync directory” to load staff.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          {data && (
            <div className="pager">
              <button disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </button>
              <span className="muted">
                Page {page} of {pages} · {data.total.toLocaleString()} people
              </span>
              <button disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </button>
            </div>
          )}
        </>
      )}
    </>
  )
}
