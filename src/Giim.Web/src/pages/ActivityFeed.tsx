import { useEffect, useState } from 'react'
import { ArrowLeft } from 'lucide-react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'

type Item = {
  kind: 'Asset' | 'Stock'
  occurredAt: string
  type: string
  summary: string
  actor: string
  ticketNumber: string | null
  assetId: string | null
  assetLabel: string | null
  note: string | null
}

/** Everything recorded under one ticket, or by one technician. */
export function ActivityFeed({ ticket, technician, onBack }: { ticket?: string; technician?: string; onBack: () => void }) {
  const nav = useNav()
  const [items, setItems] = useState<Item[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const query = ticket ? `ticket=${encodeURIComponent(ticket)}` : `actor=${encodeURIComponent(technician ?? '')}`
    api<Item[]>(`/api/activity?${query}`)
      .then(setItems)
      .catch((e: Error) => setError(e.message))
  }, [ticket, technician])

  const assets = new Set(items?.filter((i) => i.assetId).map((i) => i.assetId)).size
  const technicians = [...new Set(items?.map((i) => i.actor))]

  return (
    <>
      <button onClick={onBack} style={{ marginTop: 0, marginBottom: 12 }}>
        <ArrowLeft size={16} /> Back
      </button>
      <PageHeader
        title={ticket ? `Ticket ${ticket}` : `Activity by ${technician}`}
        subtitle={
          items
            ? `${items.length} action${items.length === 1 ? '' : 's'} · ${assets} asset${assets === 1 ? '' : 's'}` +
              (ticket ? ` · ${technicians.join(', ')}` : '') +
              (items.length >= 500 ? ' · showing the latest 500' : '')
            : undefined
        }
      />
      {error && <p className="error">{error}</p>}
      {items && items.length === 0 && <p className="muted">Nothing has been recorded {ticket ? 'under this ticket' : 'by this technician'}.</p>}
      {items && items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>When</th>
              <th>What happened</th>
              <th>Asset / item</th>
              <th>{ticket ? 'Technician' : 'Ticket'}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((i, index) => (
              <tr key={index}>
                <td className="small">{new Date(i.occurredAt).toLocaleString('en-AU', { dateStyle: 'medium', timeStyle: 'short' })}</td>
                <td>
                  {i.summary}
                  {i.note && <div className="muted small">{i.note}</div>}
                </td>
                <td>
                  {i.assetId ? (
                    <button className="link" onClick={() => nav.openAsset(i.assetId!)}>
                      {i.assetLabel}
                    </button>
                  ) : (
                    <span className="muted">Stock</span>
                  )}
                </td>
                <td>
                  {ticket ? (
                    <button className="link" onClick={() => nav.openTechnician(i.actor)}>
                      {i.actor}
                    </button>
                  ) : i.ticketNumber ? (
                    <button className="link" onClick={() => nav.openTicket(i.ticketNumber!)}>
                      {i.ticketNumber}
                    </button>
                  ) : (
                    '-'
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}
