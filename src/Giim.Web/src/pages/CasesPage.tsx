import { useEffect, useState } from 'react'
import { UserMinus, UserPlus } from 'lucide-react'
import { api } from '../api'
import { caseStatusLabel, daysUntil, relativeDay, type CaseList } from '../cases'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'
import { useUser } from '../user'
import { NewLeaverForm, NewStarterForm } from './CaseForms'

const views = [
  { key: 'Starters', label: 'Starters' },
  { key: 'Leavers', label: 'Leavers' },
  { key: 'Completed', label: 'Completed' },
  { key: 'Cancelled', label: 'Cancelled' },
] as const

type ViewKey = (typeof views)[number]['key']

const formatDate = (value: string | null) => (value ? new Date(value).toLocaleDateString('en-AU') : '-')

/** Starter and leaver checklists, soonest first. */
export function CasesPage() {
  const nav = useNav()
  const { canChange } = useUser()
  const [view, setView] = useState<ViewKey>('Starters')
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [list, setList] = useState<CaseList | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState<'starter' | 'leaver' | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => setTerm(search.trim()), 250)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    let current = true
    api<CaseList>(`/api/cases?view=${view}${term ? `&search=${encodeURIComponent(term)}` : ''}`)
      .then((l) => {
        if (!current) return
        setList(l)
        setError(null)
      })
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [view, term])

  const opened = (id: string) => {
    setCreating(null)
    nav.openCase(id)
  }

  return (
    <>
      <PageHeader
        title="Starters and leavers"
        subtitle="Checklists for people joining and leaving. Starters come from their department's profile; leavers from what they actually hold."
        actions={
          canChange && !creating ? (
            <>
              <button className="primary" onClick={() => setCreating('starter')}>
                <UserPlus size={16} /> New starter
              </button>
              <button onClick={() => setCreating('leaver')}>
                <UserMinus size={16} /> New leaver
              </button>
            </>
          ) : undefined
        }
      />

      {creating === 'starter' && <NewStarterForm onCreated={opened} onCancel={() => setCreating(null)} />}
      {creating === 'leaver' && <NewLeaverForm onCreated={opened} onCancel={() => setCreating(null)} />}

      <div className="tabs" role="tablist">
        {views.map((v) => (
          <button key={v.key} role="tab" aria-selected={view === v.key} className={view === v.key ? 'selected' : ''} onClick={() => setView(v.key)}>
            {v.label}
            {list && <span className="tab-count">{list.counts[v.key] ?? 0}</span>}
          </button>
        ))}
      </div>

      <div className="form-row filters">
        <input type="search" aria-label="Search checklists" placeholder="Search name, employee ID or ticket" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>

      {error && <p className="error">{error}</p>}
      {!list && !error && <p className="muted">Loading…</p>}
      {list && (
        <table>
          <thead>
            <tr>
              <th>Person</th>
              <th>Department</th>
              <th>{view === 'Leavers' ? 'Last day' : view === 'Starters' ? 'Starts' : 'Date'}</th>
              <th>Progress</th>
              <th>Status</th>
              <th>Ticket</th>
            </tr>
          </thead>
          <tbody>
            {list.items.map((c) => {
              const soon = c.dueDate && (view === 'Starters' || view === 'Leavers') && daysUntil(c.dueDate) < 3
              return (
                <tr key={c.id} className="clickable" onClick={() => nav.openCase(c.id)}>
                  <td>
                    <button className="link">{c.person}</button>
                    {(view === 'Completed' || view === 'Cancelled') && <div className="muted small">{c.type === 'Onboarding' ? 'Starter' : 'Leaver'}</div>}
                  </td>
                  <td>{c.department ?? '-'}</td>
                  <td>
                    {formatDate(c.dueDate)}
                    {(view === 'Starters' || view === 'Leavers') && <div className={`small ${soon ? 'error' : 'muted'}`}>{relativeDay(c.dueDate)}</div>}
                  </td>
                  <td>
                    <Progress done={c.finished} total={c.tasks} />
                  </td>
                  <td>
                    <span className={`status case-${c.status}`}>{caseStatusLabel[c.status] ?? c.status}</span>
                  </td>
                  <td>{c.ticketNumber ?? '-'}</td>
                </tr>
              )
            })}
            {list.items.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  {view === 'Starters' ? 'No starters in progress.' : view === 'Leavers' ? 'No leavers in progress.' : 'Nothing here.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </>
  )
}

export function Progress({ done, total }: { done: number; total: number }) {
  const percent = total === 0 ? 0 : Math.round((done / total) * 100)
  return (
    <span className="progress" title={`${done} of ${total} tasks done`}>
      <span className="progress-track">
        <span className="progress-bar" style={{ width: `${percent}%` }} />
      </span>
      <span className="small">
        {done}/{total}
      </span>
    </span>
  )
}
