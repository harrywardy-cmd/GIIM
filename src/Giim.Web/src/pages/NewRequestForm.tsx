import { useEffect, useState } from 'react'
import { api } from '../api'
import { priorities, reasonLabel } from '../requests'
import { searchUrl, useSearch } from '../search'
import { type PersonOption } from './AssignReturnForms'

type PersonWithManager = PersonOption & { manager: { id: string; displayName: string } | null }
type Category = { id: string; name: string; isActive: boolean }

/** Raises a device request. The recipient's manager approves unless someone else is chosen. */
export function NewRequestForm({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const [categories, setCategories] = useState<Category[]>([])
  const [recipientTerm, setRecipientTerm] = useState('')
  const [recipient, setRecipient] = useState<PersonWithManager | null>(null)
  const [approverTerm, setApproverTerm] = useState('')
  const [approver, setApprover] = useState<PersonOption | null>(null)
  const [choosingApprover, setChoosingApprover] = useState(false)
  const [form, setForm] = useState({
    categoryId: '',
    deviceDescription: '',
    specifications: '',
    reasonType: 'NewStarter',
    reason: '',
    priority: 'Medium',
    neededBy: '',
    estimatedCost: '',
    budgetCode: '',
    ticketNumber: '',
    notes: '',
  })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const recipients = useSearch<PersonWithManager>(searchUrl(recipientTerm, '/api/people?pageSize=8&search='))
  const approvers = useSearch<PersonOption>(searchUrl(approverTerm, '/api/people?status=Active&pageSize=8&search='))
  const set = (field: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [field]: e.target.value })

  useEffect(() => {
    api<Category[]>('/api/categories')
      .then((c) => setCategories(c.filter((x) => x.isActive)))
      .catch(() => setCategories([]))
  }, [])

  const submit = async () => {
    if (!recipient) return
    setSaving(true)
    try {
      const created = await api<{ id: string }>('/api/requests', {
        method: 'POST',
        json: {
          recipientPersonId: recipient.id,
          categoryId: form.categoryId,
          deviceDescription: form.deviceDescription,
          specifications: form.specifications || null,
          reasonType: form.reasonType,
          reason: form.reason,
          priority: form.priority,
          neededBy: form.neededBy || null,
          estimatedCost: form.estimatedCost ? Number(form.estimatedCost) : null,
          budgetCode: form.budgetCode || null,
          ticketNumber: form.ticketNumber || null,
          notes: form.notes || null,
          approverPersonId: approver?.id ?? null,
        },
      })
      onCreated(created.id)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const ready = recipient && form.categoryId && form.deviceDescription.trim() && form.reason.trim()

  return (
    <section className="panel">
      <h3>New device request</h3>

      <div className="form-row">
        {recipient ? (
          <p style={{ margin: 0 }}>
            For <strong>{recipient.displayName}</strong>{' '}
            <span className="muted small">
              {recipient.userPrincipalName} · {recipient.department}
            </span>{' '}
            <button
              className="link"
              onClick={() => {
                setRecipient(null)
                setApprover(null)
              }}
            >
              change
            </button>
          </p>
        ) : (
          <label className="stacked grow">
            Who is the device for?
            <input value={recipientTerm} onChange={(e) => setRecipientTerm(e.target.value)} placeholder="Type a name or email" autoFocus />
            {recipients.length > 0 && (
              <ul className="picker">
                {recipients.map((p) => (
                  <li key={p.id}>
                    <button disabled={p.status === 'Left'} onClick={() => setRecipient(p)}>
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
      </div>

      {recipient && (
        <p className="small">
          Approver:{' '}
          <strong>{approver?.displayName ?? recipient.manager?.displayName ?? 'an administrator (no manager on record)'}</strong>
          {!approver && recipient.manager && <span className="muted"> (their manager)</span>}{' '}
          {!choosingApprover && (
            <button className="link" onClick={() => setChoosingApprover(true)}>
              choose someone else
            </button>
          )}
        </p>
      )}
      {recipient && choosingApprover && (
        <div className="form-row">
          <label className="stacked grow">
            Approver
            <input value={approverTerm} onChange={(e) => setApproverTerm(e.target.value)} placeholder="Type a name or email" />
            {approvers.length > 0 && (
              <ul className="picker">
                {approvers.map((p) => (
                  <li key={p.id}>
                    <button
                      disabled={p.id === recipient.id}
                      onClick={() => {
                        setApprover(p)
                        setChoosingApprover(false)
                        setApproverTerm('')
                      }}
                    >
                      {p.displayName} <span className="muted small">{p.department}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </label>
          <button
            onClick={() => {
              setApprover(null)
              setChoosingApprover(false)
            }}
          >
            Use their manager
          </button>
        </div>
      )}

      <div className="form-row">
        <label>
          Device type
          <select value={form.categoryId} onChange={set('categoryId')}>
            <option value="">Choose…</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label className="grow">
          Device requested
          <input value={form.deviceDescription} onChange={set('deviceDescription')} placeholder="e.g. Dell Latitude 7455" />
        </label>
        <label className="grow">
          Specifications
          <input value={form.specifications} onChange={set('specifications')} placeholder="e.g. 16GB RAM, 512GB SSD, Windows 11 Pro" />
        </label>
      </div>

      <div className="form-row">
        <label>
          Reason
          <select value={form.reasonType} onChange={set('reasonType')}>
            {Object.entries(reasonLabel).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label className="grow">
          Details
          <input value={form.reason} onChange={set('reason')} placeholder="e.g. New starter in Sales, starts 12 October" />
        </label>
        <label>
          Priority
          <select value={form.priority} onChange={set('priority')}>
            {priorities.map((p) => (
              <option key={p}>{p}</option>
            ))}
          </select>
        </label>
        <label>
          Needed by
          <input type="date" value={form.neededBy} onChange={set('neededBy')} />
        </label>
      </div>

      <div className="form-row">
        <label>
          Estimated cost ($)
          <input type="number" min={0} step="0.01" value={form.estimatedCost} onChange={set('estimatedCost')} style={{ width: 130 }} />
        </label>
        <label>
          Budget / cost centre
          <input value={form.budgetCode} onChange={set('budgetCode')} placeholder="e.g. SALES-2026" />
        </label>
        <label>
          Ticket
          <input value={form.ticketNumber} onChange={set('ticketNumber')} placeholder="e.g. REQ54321" />
        </label>
        <label className="grow">
          Notes
          <input value={form.notes} onChange={set('notes')} placeholder="e.g. Needs to be set up before 12 October" />
        </label>
      </div>

      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={submit} disabled={!ready || saving}>
        {saving ? 'Submitting…' : 'Submit request'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </section>
  )
}
