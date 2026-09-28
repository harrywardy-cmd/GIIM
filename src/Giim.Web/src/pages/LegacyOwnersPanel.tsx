import { useEffect, useState } from 'react'
import { api } from '../api'

type Candidate = {
  id: string
  displayName: string
  userPrincipalName: string | null
  department: string | null
  jobTitle: string | null
  status: string
}

type Unresolved = {
  assetId: string
  assetTag: string | null
  serialNumber: string
  device: string
  legacyOwner: string
  legacyDepartment: string | null
  intuneUser: string | null
  outcome: 'Ambiguous' | 'NotFound'
  candidates: Candidate[]
}

type Preview = { unlinked: number; canLinkAutomatically: number; ambiguous: number; notFound: number; unresolved: Unresolved[] }

export function LegacyOwnersPanel({ onOpenPerson }: { onOpenPerson: (id: string) => void }) {
  const [preview, setPreview] = useState<Preview | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api<Preview>('/api/people/legacy-owners?take=300')
      .then((p) => {
        setPreview(p)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  const linkAll = async () => {
    setBusy(true)
    try {
      const r = await api<{ linked: number }>('/api/people/legacy-owners/link-automatic', { method: 'POST' })
      setMessage(`Linked ${r.linked.toLocaleString()} assets to their owners.`)
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const resolve = async (assetId: string, personId: string) => {
    try {
      await api(`/api/people/legacy-owners/${assetId}/resolve`, { method: 'POST', json: { personId } })
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  if (!preview) return error ? <p className="error">{error}</p> : <p className="muted">Checking…</p>

  return (
    <>
      <p className="muted">
        Assets imported from the old spreadsheet only have the owner’s name. This links them to the real person. Shared
        names are matched using Intune’s user and the spreadsheet’s department; anything still unclear is left for you to
        choose. Nothing is ever guessed.
      </p>

      <div className="stats">
        <Stat label="Not yet linked" value={preview.unlinked} />
        <Stat label="Certain match" value={preview.canLinkAutomatically} />
        <Stat label="Needs you to choose" value={preview.ambiguous} warn />
        <Stat label="Name not in directory" value={preview.notFound} warn />
      </div>

      {message && <p className="success">{message}</p>}
      {error && <p className="error">{error}</p>}

      <button className="primary" onClick={linkAll} disabled={busy || preview.canLinkAutomatically === 0}>
        {busy ? 'Linking…' : `Link ${preview.canLinkAutomatically.toLocaleString()} certain matches`}
      </button>

      {preview.unresolved.length > 0 && (
        <>
          <h3>Choose the owner</h3>
          <table>
            <thead>
              <tr>
                <th>Asset</th>
                <th>Spreadsheet says</th>
                <th>Who is it?</th>
              </tr>
            </thead>
            <tbody>
              {preview.unresolved.map((u) => (
                <tr key={u.assetId}>
                  <td>
                    {u.assetTag ?? u.serialNumber}
                    <div className="muted small">{u.device}</div>
                  </td>
                  <td>
                    {u.legacyOwner}
                    <div className="muted small">
                      {u.legacyDepartment ?? 'no department'}
                      {u.intuneUser && <> · Intune: {u.intuneUser}</>}
                    </div>
                    {u.outcome === 'NotFound' && <div className="small error">Not in the directory</div>}
                  </td>
                  <td>
                    {u.candidates.map((c) => (
                      <div key={c.id} className="candidate">
                        <button onClick={() => resolve(u.assetId, c.id)}>This one</button>{' '}
                        <button className="link" onClick={() => onOpenPerson(c.id)}>
                          {c.displayName}
                        </button>
                        <span className="muted small">
                          {' '}
                          {c.userPrincipalName} · {c.department} · {c.jobTitle}
                          {c.status !== 'Active' && ` · ${c.status}`}
                        </span>
                      </div>
                    ))}
                    {u.candidates.length === 0 && <span className="muted small">No suggestions</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {preview.ambiguous + preview.notFound > preview.unresolved.length && (
            <p className="muted small">Showing the first {preview.unresolved.length}; the list refreshes as you resolve them.</p>
          )}
        </>
      )}
    </>
  )
}

function Stat({ label, value, warn }: { label: string; value: number; warn?: boolean }) {
  return (
    <div className={`stat ${warn && value > 0 ? 'stat-warn' : ''}`}>
      <div className="stat-value">{value.toLocaleString()}</div>
      <div className="muted">{label}</div>
    </div>
  )
}
