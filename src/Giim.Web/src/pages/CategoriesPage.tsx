import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { useUser } from '../user'
import { PageHeader } from '../PageHeader'

type Category = {
  id: string
  name: string
  isIntuneManaged: boolean
  returnOnOffboarding: boolean
  isActive: boolean
  assetCount: number
}

export function CategoriesPage() {
  const { canAdminister } = useUser()
  const [categories, setCategories] = useState<Category[]>([])
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [intune, setIntune] = useState(false)
  const [returned, setReturned] = useState(true)

  const [refresh, setRefresh] = useState(0)
  const load = useCallback(() => setRefresh((n) => n + 1), [])

  useEffect(() => {
    api<Category[]>('/api/categories')
      .then((c) => {
        setCategories(c)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  const update = async (c: Category, change: Partial<Category>) => {
    try {
      await api(`/api/categories/${c.id}`, { method: 'PUT', json: { ...c, ...change } })
      load()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const add = async () => {
    try {
      await api('/api/categories', {
        method: 'POST',
        json: { name, isIntuneManaged: intune, returnOnOffboarding: returned },
      })
      setName('')
      setIntune(false)
      setReturned(true)
      load()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <>
      <PageHeader
        title="Asset categories"
        subtitle="Kinds of kit with serial numbers. Things without serials belong on the Stock page instead."
      />
      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Category</th>
            <th>Assets</th>
            <th>Managed in Intune</th>
            <th>Return when leaving</th>
            <th>Active</th>
          </tr>
        </thead>
        <tbody>
          {categories.map((c) => (
            <tr key={c.id} className={c.isActive ? '' : 'inactive'}>
              <td>{c.name}</td>
              <td>{c.assetCount.toLocaleString()}</td>
              <td>
                <input
                  type="checkbox"
                  aria-label={`${c.name} managed in Intune`}
                  checked={c.isIntuneManaged}
                  disabled={!canAdminister}
                  onChange={(e) => update(c, { isIntuneManaged: e.target.checked })}
                />
              </td>
              <td>
                <input
                  type="checkbox"
                  aria-label={`${c.name} returned when leaving`}
                  checked={c.returnOnOffboarding}
                  disabled={!canAdminister}
                  onChange={(e) => update(c, { returnOnOffboarding: e.target.checked })}
                />
              </td>
              <td>
                <input
                  type="checkbox"
                  aria-label={`${c.name} active`}
                  checked={c.isActive}
                  disabled={!canAdminister}
                  onChange={(e) => update(c, { isActive: e.target.checked })}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <section className="panel">
        <h3>Add category</h3>
        <div className="form-row">
          <label>
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Standing desk converter" />
          </label>
          <label className="inline">
            <input type="checkbox" checked={intune} onChange={(e) => setIntune(e.target.checked)} /> Managed in Intune
          </label>
          <label className="inline">
            <input type="checkbox" checked={returned} onChange={(e) => setReturned(e.target.checked)} /> Return when leaving
          </label>
        </div>
        <button className="primary" disabled={!name.trim() || !canAdminister} onClick={add}>
          Add category
        </button>
      </section>
    </>
  )
}
