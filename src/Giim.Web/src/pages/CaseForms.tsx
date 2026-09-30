import { useEffect, useState } from 'react'
import { api } from '../api'
import { searchUrl, useSearch } from '../search'
import { type PersonOption } from './AssignReturnForms'

type Department = { id: string; code: string; name: string }
type ProfileSummary = { departmentId: string; department: string; profiles: { id: string; name: string; jobTitle: string | null; track: string; items: number }[] }
type PersonResult = PersonOption & { assetCount?: number }

function PersonPicker({ label, status, onPick }: { label: string; status?: string; onPick: (p: PersonResult) => void }) {
  const [term, setTerm] = useState('')
  const results = useSearch<PersonResult>(searchUrl(term, `/api/people?pageSize=8${status ? `&status=${status}` : ''}&search=`))
  return (
    <label className="stacked grow">
      {label}
      <input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Type a name, email or employee ID" autoFocus />
      {results.length > 0 && (
        <ul className="picker">
          {results.map((p) => (
            <li key={p.id}>
              <button disabled={p.status === 'Left'} onClick={() => onPick(p)}>
                {p.displayName}
                <span className="muted small">
                  {' '}
                  {p.userPrincipalName ?? 'no account yet'} · {p.department}
                  {p.status !== 'Active' && ` · ${p.status}`}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </label>
  )
}

/**
 * New starter: someone already in the directory, or a new person from HR's details (they're added as pending and
 * matched by employee ID when their account appears). The checklist comes from their department's starter profile.
 */
export function NewStarterForm({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const [mode, setMode] = useState<'new' | 'existing'>('new')
  const [person, setPerson] = useState<PersonResult | null>(null)
  const [manager, setManager] = useState<PersonResult | null>(null)
  const [departments, setDepartments] = useState<Department[]>([])
  const [profiles, setProfiles] = useState<ProfileSummary[]>([])
  const [f, setF] = useState({ displayName: '', employeeId: '', departmentId: '', jobTitle: '', email: '', startDate: '', track: 'Full', profileId: '', ticketNumber: '', notes: '' })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })

  useEffect(() => {
    Promise.all([api<Department[]>('/api/departments'), api<ProfileSummary[]>('/api/profiles')])
      .then(([d, p]) => {
        setDepartments(d)
        setProfiles(p)
      })
      .catch((e: Error) => setError(e.message))
  }, [])

  const departmentProfiles = profiles.find((p) => p.departmentId === f.departmentId)?.profiles ?? []
  const existingDepartment = person ? profiles.find((p) => p.department === person.department) : undefined
  const choices = mode === 'new' ? departmentProfiles : (existingDepartment?.profiles ?? [])

  const submit = async () => {
    setSaving(true)
    setError(null)
    try {
      const created = await api<{ id: string }>('/api/cases/onboarding', {
        method: 'POST',
        json: {
          personId: mode === 'existing' ? person?.id : null,
          newPerson:
            mode === 'new'
              ? {
                  employeeId: f.employeeId,
                  displayName: f.displayName,
                  departmentId: f.departmentId,
                  jobTitle: f.jobTitle || null,
                  managerId: manager?.id ?? null,
                  email: f.email || null,
                }
              : null,
          startDate: f.startDate,
          track: f.track,
          profileId: f.profileId || null,
          ticketNumber: f.ticketNumber || null,
          notes: f.notes || null,
        },
      })
      onCreated(created.id)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const ready = f.startDate && (mode === 'existing' ? !!person : f.displayName.trim() && f.employeeId.trim() && f.departmentId)

  return (
    <section className="panel">
      <h3>New starter</h3>
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={mode === 'new'} className={mode === 'new' ? 'selected' : ''} onClick={() => setMode('new')}>
          Not in the directory yet
        </button>
        <button role="tab" aria-selected={mode === 'existing'} className={mode === 'existing' ? 'selected' : ''} onClick={() => setMode('existing')}>
          Already in the directory
        </button>
      </div>

      {mode === 'new' ? (
        <>
          <div className="form-row">
            <label className="grow">
              Name
              <input value={f.displayName} onChange={set('displayName')} placeholder="e.g. Sam Starter" autoFocus />
            </label>
            <label>
              Employee ID (from HR)
              <input value={f.employeeId} onChange={set('employeeId')} placeholder="e.g. E104233" />
            </label>
            <label>
              Department
              <select value={f.departmentId} onChange={(e) => setF({ ...f, departmentId: e.target.value, profileId: '' })}>
                <option value="">Choose…</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="grow">
              Job title
              <input value={f.jobTitle} onChange={set('jobTitle')} />
            </label>
          </div>
          <div className="form-row">
            {manager ? (
              <p style={{ margin: 0 }}>
                Manager <strong>{manager.displayName}</strong>{' '}
                <button className="link" onClick={() => setManager(null)}>
                  change
                </button>
              </p>
            ) : (
              <PersonPicker label="Manager (approves their devices)" status="Active" onPick={setManager} />
            )}
            <label className="grow">
              Email, if already known
              <input value={f.email} onChange={set('email')} placeholder="optional" />
            </label>
          </div>
        </>
      ) : person ? (
        <p>
          <strong>{person.displayName}</strong> <span className="muted small">{person.department}</span>{' '}
          <button className="link" onClick={() => setPerson(null)}>
            change
          </button>
        </p>
      ) : (
        <div className="form-row">
          <PersonPicker label="Starter" onPick={setPerson} />
        </div>
      )}

      <div className="form-row">
        <label>
          Start date
          <input type="date" value={f.startDate} onChange={set('startDate')} />
        </label>
        <label>
          Track
          <select value={f.track} onChange={set('track')}>
            <option value="Full">Full (laptop, mailbox)</option>
            <option value="Light">Light (floor / frontline)</option>
          </select>
        </label>
        <label className="grow">
          Starter profile
          <select value={f.profileId} onChange={set('profileId')}>
            <option value="">Choose automatically (job title, then track)</option>
            {choices.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
                {p.jobTitle ? ` (${p.jobTitle})` : ''} · {p.items} items
              </option>
            ))}
          </select>
        </label>
        <label>
          Ticket
          <input value={f.ticketNumber} onChange={set('ticketNumber')} placeholder="e.g. REQ54321" />
        </label>
      </div>
      <div className="form-row">
        <label className="grow">
          Notes
          <input value={f.notes} onChange={set('notes')} placeholder="e.g. Starts at the Sydney office; needs a desk phone" />
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={!ready || saving} onClick={submit}>
        {saving ? 'Creating…' : 'Create starter checklist'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </section>
  )
}

/** New leaver: the checklist is built from what they actually hold. */
export function NewLeaverForm({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const [person, setPerson] = useState<PersonResult | null>(null)
  const [f, setF] = useState({ lastDay: '', ticketNumber: '', notes: '' })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })

  const submit = async () => {
    if (!person) return
    setSaving(true)
    setError(null)
    try {
      const created = await api<{ id: string }>('/api/cases/offboarding', {
        method: 'POST',
        json: { personId: person.id, lastDay: f.lastDay || null, ticketNumber: f.ticketNumber || null, notes: f.notes || null },
      })
      onCreated(created.id)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="panel">
      <h3>New leaver</h3>
      {person ? (
        <p>
          <strong>{person.displayName}</strong> <span className="muted small">{person.department}</span>
          {person.assetCount !== undefined && (
            <span className="muted small">
              {' '}
              · holds {person.assetCount} asset{person.assetCount === 1 ? '' : 's'}
            </span>
          )}{' '}
          <button className="link" onClick={() => setPerson(null)}>
            change
          </button>
        </p>
      ) : (
        <div className="form-row">
          <PersonPicker label="Who is leaving?" onPick={setPerson} />
        </div>
      )}
      <div className="form-row">
        <label>
          Last day
          <input type="date" value={f.lastDay} onChange={set('lastDay')} />
        </label>
        <label>
          Ticket
          <input value={f.ticketNumber} onChange={set('ticketNumber')} placeholder="e.g. REQ54321" />
        </label>
        <label className="grow">
          Notes
          <input value={f.notes} onChange={set('notes')} placeholder="e.g. Manager collecting the laptop" />
        </label>
      </div>
      <p className="muted small">The checklist lists every asset and app they hold today; returning an asset ticks its task off.</p>
      {error && <p className="error">{error}</p>}
      <button className="primary" disabled={!person || !f.lastDay || saving} onClick={submit}>
        {saving ? 'Creating…' : 'Create leaver checklist'}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </section>
  )
}
