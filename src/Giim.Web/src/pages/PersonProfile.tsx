import { useEffect, useState } from 'react'
import { api } from '../api'
import { useNav } from '../nav'
import { RequestStatusBadge } from '../RequestBadges'
import { requestReference, type RequestList } from '../requests'
import { StatusBadge } from '../StatusBadge'
import { AssetDetailsPage } from './AssetDetailsPage'

type Profile = {
  id: string
  employeeId: string
  displayName: string
  userPrincipalName: string | null
  department: string | null
  jobTitle: string | null
  location: string | null
  status: string
  track: string
  startDate: string | null
  endDate: string | null
  lastSyncedAt: string | null
  manager: { id: string; displayName: string } | null
  directReports: number
  assets: { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; category: string; status: string }[]
  history: {
    id: string
    assetId: string
    assetTag: string | null
    manufacturer: string
    model: string
    assignedAt: string
    endedAt: string | null
    assignedBy: string | null
    notes: string | null
    serviceDeskRequestId: string | null
  }[]
}

const date = (v: string | null) => (v ? new Date(v).toLocaleDateString('en-AU') : '-')

export function PersonProfile({
  personId,
  onBack,
  onOpenPerson,
}: {
  personId: string
  onBack: () => void
  onOpenPerson: (id: string) => void
}) {
  const [person, setPerson] = useState<Profile | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [openAsset, setOpenAsset] = useState<string | null>(null)
  const [requests, setRequests] = useState<RequestList | null>(null)
  const nav = useNav()

  useEffect(() => {
    api<RequestList>(`/api/requests?personId=${personId}&pageSize=20`)
      .then(setRequests)
      .catch(() => setRequests(null))
  }, [personId])

  useEffect(() => {
    if (openAsset) return
    api<Profile>(`/api/people/${personId}`)
      .then(setPerson)
      .catch((e: Error) => setError(e.message))
  }, [personId, openAsset])

  if (openAsset) return <AssetDetailsPage assetId={openAsset} onBack={() => setOpenAsset(null)} />
  if (!person) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>

  return (
    <>
      <button onClick={onBack}>← Back to people</button>
      <div className="details-header">
        <div>
          <h2>{person.displayName}</h2>
          <p className="muted">
            {person.jobTitle ?? 'No job title'} · {person.department ?? 'No department'} · {person.userPrincipalName}
          </p>
        </div>
        <span className={`status person-${person.status}`}>{person.status}</span>
      </div>

      {person.status === 'Left' && person.assets.length > 0 && (
        <p className="error">
          {person.displayName} has left but still has {person.assets.length} asset{person.assets.length === 1 ? '' : 's'} recorded
          against them.
        </p>
      )}

      <div className="details-grid">
        <section className="panel">
          <h3>Details</h3>
          <dl className="facts">
            <dt>Employee ID</dt>
            <dd>{person.employeeId}</dd>
            <dt>Manager</dt>
            <dd>
              {person.manager ? (
                <button className="link" onClick={() => onOpenPerson(person.manager!.id)}>
                  {person.manager.displayName}
                </button>
              ) : (
                '-'
              )}
            </dd>
            <dt>Direct reports</dt>
            <dd>{person.directReports}</dd>
            <dt>Location</dt>
            <dd>{person.location ?? '-'}</dd>
            <dt>Start date</dt>
            <dd>{date(person.startDate)}</dd>
            <dt>End date</dt>
            <dd>{date(person.endDate)}</dd>
            <dt>Onboarding track</dt>
            <dd>{person.track}</dd>
            <dt>Last directory sync</dt>
            <dd>{person.lastSyncedAt ? new Date(person.lastSyncedAt).toLocaleString('en-AU') : 'Added by hand'}</dd>
          </dl>
        </section>

        <section className="panel">
          <h3>Assigned assets ({person.assets.length})</h3>
          <table>
            <tbody>
              {person.assets.map((a) => (
                <tr key={a.id} className="clickable" onClick={() => setOpenAsset(a.id)}>
                  <td>
                    <button className="link" onClick={() => setOpenAsset(a.id)}>
                      {a.assetTag ?? a.serialNumber}
                    </button>
                  </td>
                  <td>
                    {a.manufacturer} {a.model}
                    <div className="muted small">{a.category}</div>
                  </td>
                  <td>
                    <StatusBadge status={a.status} />
                  </td>
                </tr>
              ))}
              {person.assets.length === 0 && (
                <tr>
                  <td className="muted">No assets.</td>
                </tr>
              )}
            </tbody>
          </table>
        </section>
      </div>

      <section className="panel">
        <h3>Device requests ({requests?.total ?? 0})</h3>
        {requests && requests.items.length > 0 ? (
          <table>
            <thead>
              <tr>
                <th>Request</th>
                <th>Device</th>
                <th>Status</th>
                <th>Requested</th>
                <th>By</th>
              </tr>
            </thead>
            <tbody>
              {requests.items.map((r) => (
                <tr key={r.id} className="clickable" onClick={() => nav.openRequest(r.id)}>
                  <td>
                    <button className="link">{requestReference(r.number)}</button>
                  </td>
                  <td>{r.deviceDescription}</td>
                  <td>
                    <RequestStatusBadge status={r.status} />
                  </td>
                  <td>{date(r.submittedAt)}</td>
                  <td>{r.requestedByName}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p className="muted small">No device requests for {person.displayName}.</p>
        )}
      </section>

      <section className="panel">
        <h3>Assignment history</h3>
        <table>
          <thead>
            <tr>
              <th>Asset</th>
              <th>From</th>
              <th>To</th>
              <th>By</th>
              <th>Ticket</th>
              <th>Notes</th>
            </tr>
          </thead>
          <tbody>
            {person.history.map((h) => (
              <tr key={h.id}>
                <td>
                  <button className="link" onClick={() => setOpenAsset(h.assetId)}>
                    {h.assetTag ?? `${h.manufacturer} ${h.model}`}
                  </button>
                </td>
                <td>{date(h.assignedAt)}</td>
                <td>{h.endedAt ? date(h.endedAt) : 'Current'}</td>
                <td>{h.assignedBy ?? '-'}</td>
                <td>{h.serviceDeskRequestId ?? '-'}</td>
                <td className="small muted">{h.notes ?? ''}</td>
              </tr>
            ))}
            {person.history.length === 0 && (
              <tr>
                <td colSpan={6} className="muted">
                  No history yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </>
  )
}
