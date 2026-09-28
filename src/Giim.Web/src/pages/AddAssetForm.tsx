import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { api } from '../api'

type Category = { id: string; name: string; isActive: boolean }

type Duplicate = { message: string; existingAssetId: string }

/**
 * Form for a device that has just arrived. Built for barcode scanners: a scanner types the serial and presses
 * Enter, so Enter moves to the next field instead of submitting half a form.
 */
export function AddAssetForm({ onCreated, onOpen, onCancel }: { onCreated: (id: string) => void; onOpen: (id: string) => void; onCancel: () => void }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [form, setForm] = useState({
    serialNumber: '',
    assetTag: '',
    categoryId: '',
    manufacturer: '',
    model: '',
    location: 'IT Store Room',
    purchaseDate: '',
    warrantyExpiry: '',
    supplier: '',
    cost: '',
    purchaseOrder: '',
    ticketNumber: '',
    notes: '',
    startAs: 'Received',
  })
  const [error, setError] = useState<string | null>(null)
  const [duplicate, setDuplicate] = useState<Duplicate | null>(null)
  const [saving, setSaving] = useState(false)
  const formRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    api<Category[]>('/api/categories')
      .then((c) => setCategories(c.filter((x) => x.isActive)))
      .catch(() => setCategories([]))
  }, [])

  const set = (field: keyof typeof form, value: string) => {
    setForm({ ...form, [field]: value })
    if (field === 'serialNumber') setDuplicate(null)
  }

  /** Enter (as sent by a barcode scanner) jumps to the next input. */
  const nextOnEnter = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    const fields = Array.from(formRef.current?.querySelectorAll<HTMLElement>('input, select') ?? [])
    fields[fields.indexOf(e.currentTarget) + 1]?.focus()
  }

  /** Check the serial as soon as it's scanned, so a duplicate is caught before the rest is typed. */
  const checkSerial = async () => {
    if (!form.serialNumber.trim()) return
    try {
      const found = await api<{ id: string }>(`/api/assets/lookup?code=${encodeURIComponent(form.serialNumber)}`)
      setDuplicate({ message: `This serial or tag is already recorded.`, existingAssetId: found.id })
    } catch {
      setDuplicate(null) // 404: not recorded yet, which is what we want
    }
  }

  const submit = async () => {
    setSaving(true)
    setError(null)
    try {
      const response = await fetch('/api/assets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...form,
          assetTag: form.assetTag || null,
          purchaseDate: form.purchaseDate || null,
          warrantyExpiry: form.warrantyExpiry || null,
          cost: form.cost ? Number(form.cost) : null,
          supplier: form.supplier || null,
          purchaseOrder: form.purchaseOrder || null,
          ticketNumber: form.ticketNumber || null,
          notes: form.notes || null,
        }),
      })
      const body = await response.json().catch(() => null)
      if (response.status === 409 && body?.existingAssetId) setDuplicate({ message: body.detail, existingAssetId: body.existingAssetId })
      else if (!response.ok) setError(body?.detail ?? `Request failed (${response.status})`)
      else onCreated(body.id)
    } finally {
      setSaving(false)
    }
  }

  const ready = form.serialNumber.trim() && form.categoryId && form.manufacturer.trim() && form.model.trim() && !duplicate

  return (
    <section className="panel" ref={formRef}>
      <h3>Add asset</h3>
      <p className="muted small">Scan or type the serial from the device label. Items without a serial number belong on the Stock page.</p>

      <div className="form-row">
        <label className="grow">
          Serial number (required)
          <input
            autoFocus
            value={form.serialNumber}
            onChange={(e) => set('serialNumber', e.target.value)}
            onBlur={checkSerial}
            onKeyDown={(e) => {
              if (e.key === 'Enter') checkSerial()
              nextOnEnter(e)
            }}
          />
        </label>
        <label>
          Asset tag
          <input value={form.assetTag} onChange={(e) => set('assetTag', e.target.value)} onKeyDown={nextOnEnter} placeholder="e.g. IT-00123" />
        </label>
      </div>

      {duplicate && (
        <p className="error">
          {duplicate.message}{' '}
          <button className="link" onClick={() => onOpen(duplicate.existingAssetId)}>
            Open the existing record
          </button>
        </p>
      )}

      <div className="form-row">
        <label>
          Category (required)
          <select value={form.categoryId} onChange={(e) => set('categoryId', e.target.value)}>
            <option value="">Choose…</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Manufacturer (required)
          <input value={form.manufacturer} onChange={(e) => set('manufacturer', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
        <label className="grow">
          Model (required)
          <input value={form.model} onChange={(e) => set('model', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
      </div>

      <div className="form-row">
        <label>
          Purchase date
          <input type="date" value={form.purchaseDate} onChange={(e) => set('purchaseDate', e.target.value)} />
        </label>
        <label>
          Warranty expires
          <input type="date" value={form.warrantyExpiry} onChange={(e) => set('warrantyExpiry', e.target.value)} />
        </label>
        <label>
          Supplier
          <input value={form.supplier} onChange={(e) => set('supplier', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
        <label>
          Cost ($)
          <input type="number" min={0} step="0.01" value={form.cost} onChange={(e) => set('cost', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
        <label>
          PO number
          <input value={form.purchaseOrder} onChange={(e) => set('purchaseOrder', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
      </div>

      <div className="form-row">
        <label>
          Location
          <input value={form.location} onChange={(e) => set('location', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
        <label>
          Ticket
          <input value={form.ticketNumber} onChange={(e) => set('ticketNumber', e.target.value)} onKeyDown={nextOnEnter} placeholder="e.g. INC54321" />
        </label>
        <label>
          Starts as
          <select value={form.startAs} onChange={(e) => set('startAs', e.target.value)}>
            <option value="Received">Received (needs setting up)</option>
            <option value="ReadyToDeploy">Ready to deploy (already set up)</option>
          </select>
        </label>
        <label className="grow">
          Notes
          <input value={form.notes} onChange={(e) => set('notes', e.target.value)} onKeyDown={nextOnEnter} />
        </label>
      </div>

      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={submit} disabled={!ready || saving}>
        {saving ? 'Saving…' : 'Add asset'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </section>
  )
}
