import { useEffect, useState } from 'react'
import { api } from '../api'
import { LocationSelect } from '../LocationSelect'

type PersonOption = { id: string; displayName: string; userPrincipalName: string | null; department: string | null; status: string }
type AssetOption = { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; category: string }
type StockOption = { id: string; name: string; total: number; locations: { locationId: string; location: string; quantity: number }[] }

type AccessoryDraft =
  | { kind: 'asset'; key: string; assetId: string; label: string }
  | { kind: 'stock'; key: string; stockItemId: string; label: string; quantity: number; fromLocationId: string; fromLocationName: string }
  | { kind: 'text'; key: string; description: string; quantity: number }

export type AccessoryLine = {
  id: string
  label: string
  status: 'Issued' | 'Returned' | 'Missing'
  accessoryAssetId: string | null
  stockItemId: string | null
}

let nextKey = 0
const key = () => String(++nextKey)

/** Debounced search; results from older keystrokes are discarded. Pass null to search for nothing. */
function useSearch<T>(target: string | null) {
  const [results, setResults] = useState<T[]>([])
  useEffect(() => {
    if (!target) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      api<T[] | { people: T[] }>(target, { signal: controller.signal })
        .then((r) => setResults(Array.isArray(r) ? r : r.people))
        .catch(() => undefined)
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [target])
  return target ? results : []
}

const searchUrl = (term: string, base: string) =>
  term.trim().length >= 2 ? `${base}${encodeURIComponent(term.trim())}` : null

export function AssignForm({
  assetId,
  expectedStatus,
  onDone,
  onCancel,
}: {
  assetId: string
  expectedStatus: string
  onDone: () => void
  onCancel: () => void
}) {
  const [personTerm, setPersonTerm] = useState('')
  const [person, setPerson] = useState<PersonOption | null>(null)
  const [locationId, setLocationId] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const [note, setNote] = useState('')
  const [accessories, setAccessories] = useState<AccessoryDraft[]>([])
  const [assetTerm, setAssetTerm] = useState('')
  const [stock, setStock] = useState<StockOption[]>([])
  const [textAccessory, setTextAccessory] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const people = useSearch<PersonOption>(searchUrl(personTerm, '/api/people?pageSize=8&search='))
  const assets = useSearch<AssetOption>(searchUrl(assetTerm, '/api/assets?status=ReadyToDeploy&pageSize=8&search='))

  useEffect(() => {
    api<StockOption[]>('/api/stock')
      .then((s) => setStock(s.filter((x) => x.total > 0)))
      .catch(() => setStock([]))
  }, [])

  const addAsset = (a: AssetOption) => {
    if (a.id === assetId || accessories.some((x) => x.kind === 'asset' && x.assetId === a.id)) return
    setAccessories([...accessories, { kind: 'asset', key: key(), assetId: a.id, label: `${a.assetTag ?? a.serialNumber} ${a.manufacturer} ${a.model}` }])
    setAssetTerm('')
  }

  const addStock = (id: string) => {
    const item = stock.find((s) => s.id === id)
    if (!item) return
    // Default to taking it from wherever there is most of it.
    const where = [...item.locations].sort((a, b) => b.quantity - a.quantity)[0]
    setAccessories([
      ...accessories,
      { kind: 'stock', key: key(), stockItemId: id, label: item.name, quantity: 1, fromLocationId: where?.locationId ?? '', fromLocationName: where?.location ?? '' },
    ])
  }

  const update = (k: string, change: Partial<AccessoryDraft>) =>
    setAccessories(accessories.map((a) => (a.key === k ? ({ ...a, ...change } as AccessoryDraft) : a)))

  const submit = async () => {
    if (!person) return
    setSaving(true)
    try {
      await api(`/api/assets/${assetId}/assign`, {
        method: 'POST',
        json: {
          personId: person.id,
          expectedStatus,
          ticketNumber: ticketNumber || null,
          note: note || null,
          locationId: locationId || null,
          accessories: accessories.map((a) =>
            a.kind === 'asset'
              ? { assetId: a.assetId }
              : a.kind === 'stock'
                ? { stockItemId: a.stockItemId, quantity: a.quantity, takeFromStockAtLocationId: a.fromLocationId || null }
                : { description: a.description, quantity: a.quantity },
          ),
        },
      })
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="action-form">
      <h3>Assign</h3>
      {person ? (
        <p>
          To <strong>{person.displayName}</strong> <span className="muted small">{person.userPrincipalName} · {person.department}</span>{' '}
          <button className="link" onClick={() => setPerson(null)}>
            change
          </button>
        </p>
      ) : (
        <label className="stacked">
          Employee
          <input value={personTerm} onChange={(e) => setPersonTerm(e.target.value)} placeholder="Type a name or email" autoFocus />
          {people.length > 0 && (
            <ul className="picker">
              {people.map((p) => (
                <li key={p.id}>
                  <button disabled={p.status === 'Left'} onClick={() => setPerson(p)}>
                    {p.displayName}
                    <span className="muted small">
                      {' '}
                      {p.userPrincipalName} · {p.department}
                      {p.status !== 'Active' && ` · ${p.status}`}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </label>
      )}

      <h4>Accessories</h4>
      {accessories.length > 0 && (
        <ul className="accessory-list">
          {accessories.map((a) => (
            <li key={a.key}>
              {a.kind === 'text' ? a.description : a.label}
              {a.kind !== 'asset' && (
                <input
                  type="number"
                  min={1}
                  className="qty"
                  aria-label="Quantity"
                  value={a.quantity}
                  onChange={(e) => update(a.key, { quantity: Math.max(1, Number(e.target.value)) })}
                />
              )}
              {a.kind === 'stock' && (
                <label className="inline small">
                  <input
                    type="checkbox"
                    checked={a.fromLocationId !== ''}
                    onChange={(e) => {
                      const item = stock.find((s) => s.id === a.stockItemId)
                      const from = e.target.checked ? item?.locations[0] : undefined
                      update(a.key, { fromLocationId: from?.locationId ?? '', fromLocationName: from?.location ?? '' })
                    }}
                  />
                  take from stock{a.fromLocationName && ` at ${a.fromLocationName}`}
                </label>
              )}
              {a.kind === 'asset' && <span className="muted small"> (assigned too)</span>}
              <button className="link" onClick={() => setAccessories(accessories.filter((x) => x.key !== a.key))}>
                remove
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="form-row">
        <label className="stacked">
          Tracked accessory (dock, monitor…)
          <input value={assetTerm} onChange={(e) => setAssetTerm(e.target.value)} placeholder="Search tag, serial or model" />
          {assets.length > 0 && (
            <ul className="picker">
              {assets.map((a) => (
                <li key={a.id}>
                  <button onClick={() => addAsset(a)}>
                    {a.assetTag ?? a.serialNumber} {a.manufacturer} {a.model} <span className="muted small">{a.category}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </label>
        <label>
          From stock
          <select value="" onChange={(e) => addStock(e.target.value)}>
            <option value="">Add a stock item…</option>
            {stock.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.total} in stock)
              </option>
            ))}
          </select>
        </label>
        <label>
          Other
          <input value={textAccessory} onChange={(e) => setTextAccessory(e.target.value)} placeholder="e.g. Privacy screen" />
        </label>
        <button
          disabled={!textAccessory.trim()}
          onClick={() => {
            setAccessories([...accessories, { kind: 'text', key: key(), description: textAccessory.trim(), quantity: 1 }])
            setTextAccessory('')
          }}
        >
          Add
        </button>
      </div>

      <div className="form-row">
        <label>
          Location
          <LocationSelect value={locationId} onChange={setLocationId} allowNone noneLabel="Leave where it is" />
        </label>
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} placeholder="e.g. INC54321" />
        </label>
        <label className="grow">
          Note
          <input value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={submit} disabled={!person || saving}>
        {saving ? 'Assigning…' : 'Assign'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </div>
  )
}

export function ReturnForm({
  assetId,
  expectedStatus,
  holderName,
  accessories,
  onDone,
  onCancel,
}: {
  assetId: string
  expectedStatus: string
  holderName: string | null
  accessories: AccessoryLine[]
  onDone: (missing: string[]) => void
  onCancel: () => void
}) {
  const [returned, setReturned] = useState<Set<string>>(() => new Set(accessories.map((a) => a.id)))
  const [condition, setCondition] = useState('Good')
  const [returnedBy, setReturnedBy] = useState(holderName ?? '')
  const [returnStockTo, setReturnStockTo] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const toggle = (id: string) => {
    const next = new Set(returned)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    setReturned(next)
  }

  const missing = accessories.filter((a) => !returned.has(a.id))
  const hasStock = accessories.some((a) => a.stockItemId && returned.has(a.id))

  const submit = async () => {
    setSaving(true)
    try {
      const result = await api<{ missing: string[] }>(`/api/assets/${assetId}/return`, {
        method: 'POST',
        json: {
          expectedStatus,
          condition,
          returnedBy: returnedBy || null,
          ticketNumber: ticketNumber || null,
          note: note || null,
          returnedAccessoryIds: [...returned],
          returnStockToLocationId: hasStock ? returnStockTo || null : null,
        },
      })
      onDone(result.missing)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="action-form">
      <h3>Return</h3>
      {accessories.length > 0 ? (
        <>
          <p className="muted small">Tick everything that came back. Anything left unticked is recorded as missing.</p>
          <ul className="accessory-list">
            {accessories.map((a) => (
              <li key={a.id}>
                <label className="inline">
                  <input type="checkbox" checked={returned.has(a.id)} onChange={() => toggle(a.id)} /> {a.label}
                  {a.accessoryAssetId && <span className="muted small"> (tracked; returned too)</span>}
                </label>
              </li>
            ))}
          </ul>
          {missing.length > 0 && <p className="error small">Missing: {missing.map((m) => m.label).join(', ')}</p>}
        </>
      ) : (
        <p className="muted small">No accessories were recorded with this asset.</p>
      )}

      <div className="form-row">
        <label>
          Condition
          <select value={condition} onChange={(e) => setCondition(e.target.value)}>
            <option value="Good">Good</option>
            <option value="Fair">Fair (normal wear)</option>
            <option value="Damaged">Damaged</option>
            <option value="Faulty">Faulty</option>
          </select>
        </label>
        <label>
          Returned by
          <input value={returnedBy} onChange={(e) => setReturnedBy(e.target.value)} />
        </label>
        {hasStock && (
          <label>
            Put stock items back at
            <LocationSelect
              value={returnStockTo}
              onChange={setReturnStockTo}
              stockOnly
              onLoaded={(l) => setReturnStockTo((current) => current || (l.find((x) => x.isActive)?.id ?? ''))}
            />
          </label>
        )}
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} placeholder="e.g. INC55421" />
        </label>
        <label className="grow">
          Note
          <input value={note} onChange={(e) => setNote(e.target.value)} />
        </label>
      </div>
      {(condition === 'Damaged' || condition === 'Faulty') && (
        <p className="muted small">Damaged or faulty devices should go to repair rather than back into stock.</p>
      )}
      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={submit} disabled={saving}>
        {saving ? 'Saving…' : missing.length ? `Return with ${missing.length} missing` : 'Return'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </div>
  )
}
