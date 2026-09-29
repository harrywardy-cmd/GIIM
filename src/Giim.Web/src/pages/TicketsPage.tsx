import { useEffect, useState } from 'react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'

type Ticket = { ticketNumber: string; actions: number; assets: number; firstAt: string; lastAt: string; technicians: string[] }

const PAGE_SIZE = 50
const date = (v: string) => new Date(v).toLocaleDateString('en-AU')

/** Every ticket number GIIM has recorded against an action, newest activity first. */
export function TicketsPage() {
  const nav = useNav()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<{ total: number; tickets: Ticket[] } | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      api<{ total: number; tickets: Ticket[] }>(
        `/api/tickets?page=${page}&pageSize=${PAGE_SIZE}&search=${encodeURIComponent(search)}`,
        { signal: controller.signal },
      )
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
  }, [search, page])

  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1

  return (
    <>
      <PageHeader title="Tickets" subtitle="Everything done under a ServiceDesk Plus ticket number: assignments, returns, repairs, moves and stock." />
      <input
        type="search"
        placeholder="Search ticket number, e.g. INC54321"
        value={search}
        onChange={(e) => {
          setSearch(e.target.value)
          setPage(1)
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && search.trim()) nav.openTicket(search.trim().toUpperCase())
        }}
      />
      {error && <p className="error">{error}</p>}
      <table>
        <thead>
          <tr>
            <th>Ticket</th>
            <th>Actions</th>
            <th>Assets</th>
            <th>Technicians</th>
            <th>First</th>
            <th>Latest</th>
          </tr>
        </thead>
        <tbody>
          {data?.tickets.map((t) => (
            <tr key={t.ticketNumber} className="clickable" onClick={() => nav.openTicket(t.ticketNumber)}>
              <td>
                <button className="link">{t.ticketNumber}</button>
              </td>
              <td>{t.actions}</td>
              <td>{t.assets}</td>
              <td className="small">{t.technicians.join(', ')}</td>
              <td className="small">{date(t.firstAt)}</td>
              <td className="small">{date(t.lastAt)}</td>
            </tr>
          ))}
          {data && data.tickets.length === 0 && (
            <tr>
              <td colSpan={6} className="muted">
                {search ? 'No tickets match.' : 'No actions have been recorded against a ticket yet. Add the ticket number when assigning, returning or repairing.'}
              </td>
            </tr>
          )}
        </tbody>
      </table>
      {data && data.total > PAGE_SIZE && (
        <div className="pager">
          <button disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Previous
          </button>
          <span className="muted">
            Page {page} of {pages} · {data.total.toLocaleString()} tickets
          </span>
          <button disabled={page >= pages} onClick={() => setPage(page + 1)}>
            Next
          </button>
        </div>
      )}
    </>
  )
}
