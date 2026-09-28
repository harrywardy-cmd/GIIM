import { useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import { api } from '../api'
import { PageHeader } from '../PageHeader'

type Location = {
  id: string
  name: string
  kind: string
  address: string | null
  holdsStock: boolean
  isActive: boolean
  assets: number
  stockOnHand: number
}

type Draft = { name: string; kind: string; address: string; holdsStock: boolean; isActive: boolean }

const kinds = [
  { value: 'Office', label: 'Office' },
  { value: 'ItStoreRoom', label: 'IT store room' },
  { value: 'DistributionCentre', label: 'Distribution centre / warehouse' },
  { value: 'Site', label: 'Site' },
  { value: 'Remote', label: 'Remote / home' },
  { value: 'Other', label: 'Other' },
]
const kindLabel = (k: string) => kinds.find((x) => x.value === k)?.label ?? k
const emptyDraft: Draft = { name: '', kind: 'Office', address: '', holdsStock: false, isActive: true }

export function LocationsPage() {
  const [locations, setLocations] = useState<Location[]>([])
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [draft, setDraft] = useState<Draft>(emptyDraft)

  useEffect(() => {
    api<Location[]>('/api/locations')
      .then((l) => {
        setLocations(l)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  const startEdit = (l: Location) => {
    setEditing(l.id)
    setDraft({ name: l.name, kind: l.kind, address: l.address ?? '', holdsStock: l.holdsStock, isActive: l.isActive })
  }

  const save = async () => {
    try {
      const json = { ...draft, address: draft.address || null }
      if (editing === 'new') await api('/api/locations', { method: 'POST', json })
      else await api(`/api/locations/${editing}`, { method: 'PUT', json })
      setEditing(null)
      setError(null)
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const formRow = (key: string) => (
    <tr key={key}>
      <td>
        <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="e.g. Melbourne Office" autoFocus />
      </td>
      <td>
        <select value={draft.kind} onChange={(e) => setDraft({ ...draft, kind: e.target.value })}>
          {kinds.map((k) => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </select>
      </td>
      <td>
        <input value={draft.address} onChange={(e) => setDraft({ ...draft, address: e.target.value })} placeholder="optional" />
      </td>
      <td>
        <input type="checkbox" aria-label="Holds stock" checked={draft.holdsStock} onChange={(e) => setDraft({ ...draft, holdsStock: e.target.checked })} />
      </td>
      <td>
        <input type="checkbox" aria-label="In use" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
      </td>
      <td colSpan={2}>
        <button className="primary" onClick={save} disabled={!draft.name.trim()} style={{ marginTop: 0 }}>
          Save
        </button>{' '}
        <button onClick={() => setEditing(null)} style={{ marginTop: 0 }}>
          Cancel
        </button>
      </td>
    </tr>
  )

  return (
    <>
      <PageHeader
        title="Locations"
        subtitle="Offices, store rooms and sites. Assets and stock link to these, so renaming a location updates everything."
        actions={
          <button
            className="primary"
            onClick={() => {
              setEditing('new')
              setDraft(emptyDraft)
            }}
          >
            <Plus size={16} /> Add location
          </button>
        }
      />
      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Kind</th>
            <th>Address</th>
            <th>Holds stock</th>
            <th>In use</th>
            <th>Assets</th>
            <th>Stock on hand</th>
          </tr>
        </thead>
        <tbody>
          {editing === 'new' && formRow('new')}
          {locations.map((l) =>
            editing === l.id ? (
              formRow(l.id)
            ) : (
              <tr key={l.id} className={`clickable ${l.isActive ? '' : 'inactive'}`} onClick={() => startEdit(l)} title="Click to edit">
                <td>
                  <button className="link">{l.name}</button>
                </td>
                <td>{kindLabel(l.kind)}</td>
                <td className="small">{l.address ?? '-'}</td>
                <td>{l.holdsStock ? 'Yes' : '-'}</td>
                <td>{l.isActive ? 'Yes' : 'Closed'}</td>
                <td>{l.assets.toLocaleString()}</td>
                <td>{l.stockOnHand.toLocaleString()}</td>
              </tr>
            ),
          )}
          {locations.length === 0 && editing !== 'new' && (
            <tr>
              <td colSpan={7} className="muted">
                No locations yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
      <p className="muted small">
        Closing a location keeps its history but stops new assets or deliveries going there. “Holds stock” adds it to the
        stock page’s location list.
      </p>
    </>
  )
}
