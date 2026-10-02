import { useEffect, useState } from 'react'
import { Upload } from 'lucide-react'
import { api } from '../api'
import { profileItemTypeLabel } from '../cases'
import { PageHeader } from '../PageHeader'
import { useUser } from '../user'

type DepartmentProfiles = {
  departmentId: string
  department: string
  code: string
  profiles: { id: string; name: string; jobTitle: string | null; track: string; items: number }[]
}

type Profile = {
  id: string
  name: string
  jobTitle: string | null
  track: string
  departmentId: string
  department: string
  items: { id: string; type: string; description: string; groupName: string | null; cloudGroup: boolean; categoryId: string | null; stockItemId: string | null; category: string | null; stockItem: string | null }[]
}

type Option = { id: string; name: string; isActive: boolean }

type ImportResult = { departmentsAdded: number; profilesAdded: number; profilesReplaced: number; items: number; warnings: string[] }

/** Starter profiles: what a new starter in each department receives. Administrators edit; everyone can view. */
export function ProfilesPage() {
  const { canAdminister } = useUser()
  const [departments, setDepartments] = useState<DepartmentProfiles[]>([])
  const [selected, setSelected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [adding, setAdding] = useState<string | null>(null)
  const [importResult, setImportResult] = useState<ImportResult | null>(null)

  useEffect(() => {
    api<DepartmentProfiles[]>('/api/profiles')
      .then((d) => {
        setDepartments(d)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  const importFile = async (file: File) => {
    const form = new FormData()
    form.append('file', file)
    try {
      setImportResult(await api<ImportResult>('/api/profiles/import', { method: 'POST', body: form }))
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const withoutProfile = departments.filter((d) => d.profiles.length === 0).length

  return (
    <>
      <PageHeader
        title="Starter profiles"
        subtitle="What a new starter in each department receives: devices, stock items, apps and groups. Starter checklists are built from these."
        actions={
          canAdminister ? (
            <label className="button">
              <Upload size={16} /> Import profiles (JSON)
              <input
                type="file"
                accept=".json,application/json"
                hidden
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  if (file) importFile(file)
                }}
              />
            </label>
          ) : undefined
        }
      />
      {error && <p className="error">{error}</p>}
      {importResult && (
        <p className="notice">
          Imported {importResult.profilesAdded} new and {importResult.profilesReplaced} updated profiles ({importResult.items} items)
          {importResult.departmentsAdded > 0 && `, and ${importResult.departmentsAdded} new departments`}.
          {importResult.warnings.length > 0 && (
            <>
              {' '}
              Not imported:
              <span className="small">
                {importResult.warnings.map((w) => (
                  <span key={w} style={{ display: 'block' }}>
                    {w}
                  </span>
                ))}
              </span>
            </>
          )}
        </p>
      )}
      {withoutProfile > 0 && (
        <p className="muted small">
          {withoutProfile} department{withoutProfile === 1 ? ' has' : 's have'} no starter profile yet; starters there need a profile chosen by hand.
        </p>
      )}

      <div className="details-grid">
        <section className="panel">
          <h3>Departments</h3>
          <ul className="plain-list">
            {departments.map((d) => (
              <li key={d.departmentId}>
                <strong>{d.department}</strong> <span className="muted small">{d.code}</span>
                {d.profiles.map((p) => (
                  <div key={p.id}>
                    <button className={`link ${selected === p.id ? 'selected-link' : ''}`} onClick={() => setSelected(p.id)}>
                      {p.name}
                    </button>{' '}
                    <span className="muted small">
                      {p.jobTitle ? `${p.jobTitle} · ` : ''}
                      {p.track} · {p.items} item{p.items === 1 ? '' : 's'}
                    </span>
                  </div>
                ))}
                {d.profiles.length === 0 && <div className="muted small">No profile</div>}
                {canAdminister &&
                  (adding === d.departmentId ? (
                    <NewProfileForm
                      department={d}
                      onDone={(id) => {
                        setAdding(null)
                        setRefresh((n) => n + 1)
                        if (id) setSelected(id)
                      }}
                    />
                  ) : (
                    <button className="link small" onClick={() => setAdding(d.departmentId)}>
                      + Add profile
                    </button>
                  ))}
              </li>
            ))}
          </ul>
        </section>
        <div>{selected ? <ProfileEditor key={selected} profileId={selected} onChanged={() => setRefresh((n) => n + 1)} onDeleted={() => setSelected(null)} /> : <p className="muted">Choose a profile to see what it includes.</p>}</div>
      </div>
    </>
  )
}

function NewProfileForm({ department, onDone }: { department: DepartmentProfiles; onDone: (id?: string) => void }) {
  const [name, setName] = useState(`${department.department} - ${department.profiles.length === 0 ? 'Default' : ''}`)
  const [jobTitle, setJobTitle] = useState('')
  const [track, setTrack] = useState('Full')
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    try {
      const created = await api<{ id: string }>('/api/profiles', {
        method: 'POST',
        json: { departmentId: department.departmentId, name, jobTitle: jobTitle || null, track },
      })
      onDone(created.id)
    } catch (e) {
      setError((e as Error).message)
    }
  }
  return (
    <div className="form-row">
      <label className="grow">
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
      </label>
      <label>
        Only for job title
        <input value={jobTitle} onChange={(e) => setJobTitle(e.target.value)} placeholder="optional" />
      </label>
      <label>
        Track
        <select value={track} onChange={(e) => setTrack(e.target.value)}>
          <option>Full</option>
          <option>Light</option>
        </select>
      </label>
      <button className="primary" onClick={save} disabled={!name.trim()}>
        Add
      </button>
      <button onClick={() => onDone()}>Cancel</button>
      {error && <p className="error">{error}</p>}
    </div>
  )
}

function ProfileEditor({ profileId, onChanged, onDeleted }: { profileId: string; onChanged: () => void; onDeleted: () => void }) {
  const { canAdminister } = useUser()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [categories, setCategories] = useState<Option[]>([])
  const [stock, setStock] = useState<Option[]>([])
  const [refresh, setRefresh] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [item, setItem] = useState({ type: 'Hardware', description: '', groupName: '', cloudGroup: false, categoryId: '', stockItemId: '' })

  useEffect(() => {
    api<Profile>(`/api/profiles/${profileId}`)
      .then(setProfile)
      .catch((e: Error) => setError(e.message))
  }, [profileId, refresh])

  useEffect(() => {
    Promise.all([api<Option[]>('/api/categories'), api<Option[]>('/api/stock')])
      .then(([c, s]) => {
        setCategories(c.filter((x) => x.isActive))
        setStock(s.filter((x) => x.isActive))
      })
      .catch(() => undefined)
  }, [])

  if (!profile) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>

  const call = async (work: () => Promise<unknown>) => {
    setError(null)
    try {
      await work()
      setRefresh((n) => n + 1)
      onChanged()
      return true
    } catch (e) {
      setError((e as Error).message)
      return false
    }
  }

  const addItem = () =>
    call(() =>
      api(`/api/profiles/${profile.id}/items`, {
        method: 'POST',
        json: {
          type: item.type,
          description: item.description,
          groupName: item.groupName || null,
          cloudGroup: needsGroup && !!item.groupName && item.cloudGroup,
          categoryId: item.categoryId || null,
          stockItemId: item.stockItemId || null,
        },
      }),
    ).then((ok) => ok && setItem({ ...item, description: '', groupName: '', cloudGroup: false }))

  const needsGroup = item.type === 'SecurityGroup' || item.type === 'LicenceGroup' || item.type === 'Application'

  return (
    <section className="panel" style={{ marginTop: 0 }}>
      <h3>{profile.name}</h3>
      <p className="muted small">
        {profile.department}
        {profile.jobTitle ? ` · only for ${profile.jobTitle}` : ''} · {profile.track} track
      </p>
      {error && <p className="error">{error}</p>}
      <table>
        <thead>
          <tr>
            <th>Type</th>
            <th>Item</th>
            <th>Details</th>
            {canAdminister && <th></th>}
          </tr>
        </thead>
        <tbody>
          {profile.items.map((i) => (
            <tr key={i.id}>
              <td>{profileItemTypeLabel[i.type] ?? i.type}</td>
              <td>{i.description}</td>
              <td className="small muted">
                {i.category ?? i.stockItem ?? i.groupName ?? (i.type === 'Application' ? 'granted by hand' : '')}
                {i.cloudGroup && <span className="tag"> cloud</span>}
              </td>
              {canAdminister && (
                <td>
                  <button className="link" onClick={() => call(() => api(`/api/profiles/${profile.id}/items/${i.id}`, { method: 'DELETE' }))}>
                    remove
                  </button>
                </td>
              )}
            </tr>
          ))}
          {profile.items.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                Nothing yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {canAdminister && (
        <>
          <h4>Add an item</h4>
          <div className="form-row">
            <label>
              Type
              <select value={item.type} onChange={(e) => setItem({ ...item, type: e.target.value })}>
                {Object.entries(profileItemTypeLabel).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            <label className="grow">
              Description
              <input value={item.description} onChange={(e) => setItem({ ...item, description: e.target.value })} placeholder="e.g. Standard laptop" />
            </label>
            {item.type === 'Hardware' && (
              <label>
                Device category
                <select value={item.categoryId} onChange={(e) => setItem({ ...item, categoryId: e.target.value })}>
                  <option value="">Choose…</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {item.type === 'StockItem' && (
              <label>
                Stock item
                <select value={item.stockItemId} onChange={(e) => setItem({ ...item, stockItemId: e.target.value })}>
                  <option value="">Choose…</option>
                  {stock.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {needsGroup && (
              <label>
                {item.type === 'Application' ? 'Access group (optional)' : 'Group name'}
                <input value={item.groupName} onChange={(e) => setItem({ ...item, groupName: e.target.value })} placeholder="e.g. APP-Xero" />
              </label>
            )}
            {needsGroup && item.groupName && (
              <label className="inline" title="Made in Entra ID rather than synced from AD: GIIM adds people to it through Graph, not the on-prem agent.">
                <input type="checkbox" checked={item.cloudGroup} onChange={(e) => setItem({ ...item, cloudGroup: e.target.checked })} /> Cloud-only group (Entra)
              </label>
            )}
            <button className="primary" onClick={addItem} disabled={!item.description.trim()}>
              Add
            </button>
          </div>
          <p>
            <button
              className="link"
              onClick={async () => {
                if (!window.confirm(`Delete the profile “${profile.name}”? Checklists already created keep their tasks.`)) return
                if (await call(() => api(`/api/profiles/${profile.id}`, { method: 'DELETE' }))) onDeleted()
              }}
            >
              Delete this profile
            </button>
          </p>
        </>
      )}
    </section>
  )
}
