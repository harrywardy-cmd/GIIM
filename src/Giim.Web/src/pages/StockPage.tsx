import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { PageHeader } from '../PageHeader'
import { useUser } from '../user'

type StockLevel = {
  id: string
  name: string
  description: string | null
  reorderLevel: number
  isActive: boolean
  total: number
  isLow: boolean
  locations: { locationId: string; location: string; quantity: number }[]
}

type StockLocation = { id: string; name: string; isActive: boolean }

type Movement = {
  id: string
  createdAt: string
  location: string
  quantity: number
  reason: string
  note: string | null
  serviceDeskRequestId: string | null
  actor: string
}

const reasons = [
  { value: 'Received', label: 'Received (delivery)' },
  { value: 'Issued', label: 'Issued to staff' },
  { value: 'Returned', label: 'Returned to store' },
  { value: 'WrittenOff', label: 'Written off (damaged / lost)' },
  { value: 'Adjustment', label: 'Adjustment (stocktake)' },
]

export function StockPage() {
  const { canChange } = useUser()
  const [levels, setLevels] = useState<StockLevel[]>([])
  const [locations, setLocations] = useState<StockLocation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<StockLevel | null>(null)
  const [lowOnly, setLowOnly] = useState(false)

  const [refresh, setRefresh] = useState(0)
  const load = useCallback(() => setRefresh((n) => n + 1), [])

  useEffect(() => {
    Promise.all([api<StockLevel[]>('/api/stock'), api<StockLocation[]>('/api/stock/locations')])
      .then(([l, locs]) => {
        setLevels(l)
        setLocations(locs)
        setError(null)
        setSelected((current) => (current ? (l.find((x) => x.id === current.id) ?? null) : null))
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  const shown = levels.filter((l) => (lowOnly ? l.isLow : true))
  const lowCount = levels.filter((l) => l.isLow).length

  return (
    <>
      <PageHeader title="Stock" subtitle="Items without serial numbers. Levels are counted from every stock movement." />
      {error && <p className="error">{error}</p>}

      <div className="stats">
        <div className="stat">
          <div className="stat-value">{levels.filter((l) => l.isActive).length}</div>
          <div className="muted">Stock items</div>
        </div>
        <div className={`stat ${lowCount > 0 ? 'stat-warn' : ''}`}>
          <div className="stat-value">{lowCount}</div>
          <div className="muted">Low stock</div>
        </div>
      </div>

      <label className="inline">
        <input type="checkbox" checked={lowOnly} onChange={(e) => setLowOnly(e.target.checked)} /> Show low stock only
      </label>

      <table>
        <thead>
          <tr>
            <th>Item</th>
            <th>On hand</th>
            <th>Reorder at</th>
            <th>By location</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {shown.map((l) => (
            <tr key={l.id} className={l.isActive ? '' : 'inactive'}>
              <td>
                {l.name}
                {l.description && <div className="muted small">{l.description}</div>}
              </td>
              <td>
                <strong>{l.total}</strong> {l.isLow && <span className="badge-warn">Low</span>}
              </td>
              <td>{l.reorderLevel}</td>
              <td className="small">{l.locations.map((x) => `${x.location}: ${x.quantity}`).join(' · ') || '-'}</td>
              <td>
                {canChange && <button onClick={() => setSelected(l)}>Manage</button>}
              </td>
            </tr>
          ))}
          {shown.length === 0 && (
            <tr>
              <td colSpan={5} className="muted">
                {lowOnly ? 'Nothing is low on stock.' : 'No stock items yet. Add one below.'}
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {selected ? (
        <ManageItem key={selected.id} item={selected} locations={locations} onChanged={load} onClose={() => setSelected(null)} />
      ) : (
        canChange && <NewItem onCreated={load} />
      )}
    </>
  )
}

function NewItem({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [reorderLevel, setReorderLevel] = useState(5)
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    try {
      await api('/api/stock/items', { method: 'POST', json: { name, description, reorderLevel } })
      setName('')
      setDescription('')
      setError(null)
      onCreated()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel">
      <h3>Add stock item</h3>
      <div className="form-row">
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Laptop bag" />
        </label>
        <label>
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="optional" />
        </label>
        <label>
          Reorder at
          <input type="number" min={0} value={reorderLevel} onChange={(e) => setReorderLevel(Number(e.target.value))} />
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={!name.trim()} onClick={save}>
        Add item
      </button>
    </section>
  )
}

function ManageItem({
  item,
  locations,
  onChanged,
  onClose,
}: {
  item: StockLevel
  locations: StockLocation[]
  onChanged: () => void
  onClose: () => void
}) {
  const [reason, setReason] = useState('Received')
  const [locationId, setLocationId] = useState(locations.find((l) => l.isActive)?.id ?? '')
  const locationName = locations.find((l) => l.id === locationId)?.name ?? ''
  const [quantity, setQuantity] = useState(1)
  const [note, setNote] = useState('')
  const [ticket, setTicket] = useState('')
  const [reorderLevel, setReorderLevel] = useState(item.reorderLevel)
  const [history, setHistory] = useState<Movement[]>([])
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const loadHistory = useCallback(
    () => api<Movement[]>(`/api/stock/items/${item.id}/movements`).then(setHistory).catch(() => setHistory([])),
    [item.id],
  )

  useEffect(() => {
    loadHistory()
  }, [loadHistory])

  const record = async () => {
    try {
      await api(`/api/stock/items/${item.id}/movements`, {
        method: 'POST',
        json: { locationId, reason, quantity, note: note || null, serviceDeskRequestId: ticket || null },
      })
      setMessage(`Recorded: ${reasons.find((r) => r.value === reason)?.label}, ${quantity} at ${locationName}.`)
      setError(null)
      setNote('')
      setTicket('')
      onChanged()
      loadHistory()
    } catch (e) {
      setError((e as Error).message)
      setMessage(null)
    }
  }

  const saveSettings = async () => {
    try {
      await api(`/api/stock/items/${item.id}`, {
        method: 'PUT',
        json: { name: item.name, description: item.description, reorderLevel, isActive: item.isActive },
      })
      setMessage('Reorder level saved.')
      setError(null)
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section className="panel">
      <h3>
        {item.name} <span className="muted">· {item.total} on hand</span>
      </h3>

      <div className="form-row">
        <label>
          What happened
          <select value={reason} onChange={(e) => setReason(e.target.value)}>
            {reasons.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Location
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)}>
            {!locationId && <option value="">Choose…</option>}
            {locations.map((l) => (
              <option key={l.id} value={l.id} disabled={!l.isActive && reason === 'Received'}>
                {l.name}
                {!l.isActive && ' (closed)'}
              </option>
            ))}
          </select>
        </label>
        <label>
          Quantity{reason === 'Adjustment' && ' (+ or -)'}
          <input type="number" value={quantity} onChange={(e) => setQuantity(Number(e.target.value))} />
        </label>
        <label>
          SDP ticket
          <input value={ticket} onChange={(e) => setTicket(e.target.value)} placeholder="optional" />
        </label>
        <label className="grow">
          Note{reason === 'Adjustment' && ' (required)'}
          <input value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      {message && <p className="success">{message}</p>}
      <button className="primary" onClick={record} disabled={!locationId || quantity === 0}>
        Record
      </button>{' '}
      <button onClick={onClose}>Close</button>

      <div className="form-row">
        <label>
          Reorder at
          <input type="number" min={0} value={reorderLevel} onChange={(e) => setReorderLevel(Number(e.target.value))} />
        </label>
        <button onClick={saveSettings} disabled={reorderLevel === item.reorderLevel}>
          Save reorder level
        </button>
      </div>

      <h3>History</h3>
      <table>
        <thead>
          <tr>
            <th>When</th>
            <th>What</th>
            <th>Change</th>
            <th>Location</th>
            <th>Ticket</th>
            <th>Note</th>
            <th>By</th>
          </tr>
        </thead>
        <tbody>
          {history.map((m) => (
            <tr key={m.id}>
              <td className="small">{new Date(m.createdAt).toLocaleString('en-AU')}</td>
              <td>{m.reason}</td>
              <td>{m.quantity > 0 ? `+${m.quantity}` : m.quantity}</td>
              <td>{m.location}</td>
              <td>{m.serviceDeskRequestId ?? '-'}</td>
              <td className="small">{m.note ?? ''}</td>
              <td className="small">{m.actor}</td>
            </tr>
          ))}
          {history.length === 0 && (
            <tr>
              <td colSpan={7} className="muted">
                No movements yet. Record a delivery to set the opening stock.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </section>
  )
}
