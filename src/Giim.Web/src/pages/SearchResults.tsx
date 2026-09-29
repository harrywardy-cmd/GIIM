import { useEffect, useState } from 'react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'
import { RequestStatusBadge } from '../RequestBadges'
import { requestReference } from '../requests'
import { StatusBadge } from '../StatusBadge'

export type SearchResponse = {
  term: string
  exactAssetId: string | null
  exactTicket: string | null
  exactRequestId: string | null
  assets: { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; category: string; status: string; assignedTo: string | null }[]
  people: { id: string; displayName: string; userPrincipalName: string | null; department: string | null; status: string; assets: number }[]
  tickets: { ticketNumber: string; actions: number; assets: number; lastAt: string }[]
  technicians: { actor: string; actions: number }[]
  requests: { id: string; number: number; deviceDescription: string; status: string; recipient: string }[]
}

/** Grouped results for the top search bar: assets, people, requests, tickets and technicians. */
export function SearchResults({ term, onSeeAllAssets }: { term: string; onSeeAllAssets: (term: string) => void }) {
  const nav = useNav()
  const [result, setResult] = useState<SearchResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api<SearchResponse>(`/api/search?q=${encodeURIComponent(term)}`)
      .then(setResult)
      .catch((e: Error) => setError(e.message))
  }, [term])

  if (!result) return error ? <p className="error">{error}</p> : <p className="muted">Searching…</p>

  const nothing =
    !result.assets.length && !result.people.length && !result.tickets.length && !result.technicians.length && !result.requests.length

  return (
    <>
      <PageHeader title={`Search: “${term}”`} subtitle="Assets, people, requests, tickets and technicians. Exact serials, asset tags, REQ numbers and ticket numbers open directly." />
      {nothing && <p className="muted">Nothing found. Try part of a serial, a surname, a model or a ticket number.</p>}

      {result.assets.length > 0 && (
        <section className="card">
          <div className="card-header">
            <h3>Assets</h3>
            <button className="link" onClick={() => onSeeAllAssets(term)}>
              See all matching assets
            </button>
          </div>
          <table>
            <tbody>
              {result.assets.map((a) => (
                <tr key={a.id} className="clickable" onClick={() => nav.openAsset(a.id)}>
                  <td>
                    <button className="link">{a.assetTag ?? a.serialNumber}</button>
                    <div className="muted small">S/N {a.serialNumber}</div>
                  </td>
                  <td>
                    {a.manufacturer} {a.model}
                    <div className="muted small">{a.category}</div>
                  </td>
                  <td>
                    <StatusBadge status={a.status} />
                  </td>
                  <td>{a.assignedTo ?? '-'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {result.requests.length > 0 && (
        <section className="card">
          <div className="card-header">
            <h3>Device requests</h3>
          </div>
          <table>
            <tbody>
              {result.requests.map((r) => (
                <tr key={r.id} className="clickable" onClick={() => nav.openRequest(r.id)}>
                  <td>
                    <button className="link">{requestReference(r.number)}</button>
                  </td>
                  <td>{r.deviceDescription}</td>
                  <td>{r.recipient}</td>
                  <td>
                    <RequestStatusBadge status={r.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {result.people.length > 0 && (
        <section className="card">
          <div className="card-header">
            <h3>People</h3>
          </div>
          <table>
            <tbody>
              {result.people.map((p) => (
                <tr key={p.id} className="clickable" onClick={() => nav.openPerson(p.id)}>
                  <td>
                    <button className="link">{p.displayName}</button>
                    <div className="muted small">{p.userPrincipalName}</div>
                  </td>
                  <td>{p.department ?? '-'}</td>
                  <td>
                    <span className={`status person-${p.status}`}>{p.status}</span>
                  </td>
                  <td>
                    {p.assets} asset{p.assets === 1 ? '' : 's'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      <div className="dash-grid" style={{ marginTop: 20 }}>
        {result.tickets.length > 0 && (
          <section className="card">
            <div className="card-header">
              <h3>Tickets</h3>
            </div>
            <ul className="attention">
              {result.tickets.map((t) => (
                <li key={t.ticketNumber}>
                  <button className="link" onClick={() => nav.openTicket(t.ticketNumber)}>
                    {t.ticketNumber}
                  </button>
                  <span className="muted small">
                    {t.actions} action{t.actions === 1 ? '' : 's'} · {t.assets} asset{t.assets === 1 ? '' : 's'}
                  </span>
                  <span className="count small">{new Date(t.lastAt).toLocaleDateString('en-AU')}</span>
                </li>
              ))}
            </ul>
          </section>
        )}
        {result.technicians.length > 0 && (
          <section className="card">
            <div className="card-header">
              <h3>Technicians</h3>
            </div>
            <ul className="attention">
              {result.technicians.map((t) => (
                <li key={t.actor}>
                  <button className="link" onClick={() => nav.openTechnician(t.actor)}>
                    {t.actor}
                  </button>
                  <span className="count small">{t.actions.toLocaleString()} actions</span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </>
  )
}
