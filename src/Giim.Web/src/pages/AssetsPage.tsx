import { useEffect, useState } from 'react'
import { Plus, Printer } from 'lucide-react'
import { api } from '../api'
import { assetIdFromLink } from '../links'
import { LocationSelect } from '../LocationSelect'
import { statusLabel } from '../status'
import { useUser } from '../user'
import { PageHeader } from '../PageHeader'
import { StatusBadge } from '../StatusBadge'
import { AddAssetForm } from './AddAssetForm'
import { AssetDetailsPage } from './AssetDetailsPage'
import { LabelSheet } from './LabelSheet'

type Asset = {
  id: string
  assetTag: string | null
  serialNumber: string
  manufacturer: string
  model: string
  category: string
  status: string
  location: string | null
  assignedTo: string | null
  legacyAssignedTo: string | null
}

export function AssetsPage({ initialSearch = '' }: { initialSearch?: string }) {
  const [search, setSearch] = useState(initialSearch)
  const [assets, setAssets] = useState<Asset[]>([])
  const [error, setError] = useState<string | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [notFound, setNotFound] = useState<string | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [printing, setPrinting] = useState<string[] | null>(null)
  const { canChange } = useUser()
  const [status, setStatus] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [categories, setCategories] = useState<{ id: string; name: string }[]>([])

  useEffect(() => {
    api<{ id: string; name: string }[]>('/api/categories')
      .then(setCategories)
      .catch(() => setCategories([]))
  }, [])

  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]))

  /** Enter (or a barcode scanner) on an exact serial, asset tag or QR label opens that device directly. */
  const lookup = async () => {
    if (!search.trim()) return
    const linked = assetIdFromLink(search)
    if (linked) {
      setOpenId(linked)
      return
    }
    try {
      const found = await api<{ id: string }>(`/api/assets/lookup?code=${encodeURIComponent(search)}`)
      setNotFound(null)
      setOpenId(found.id)
    } catch {
      setNotFound(search.trim())
    }
  }

  useEffect(() => {
    if (openId) return
    const controller = new AbortController()
    const params = new URLSearchParams({ search })
    if (status) params.set('status', status)
    if (categoryId) params.set('categoryId', categoryId)
    if (locationId) params.set('locationId', locationId)
    const timer = setTimeout(() => {
      api<Asset[]>(`/api/assets?${params}`, { signal: controller.signal })
        .then((data) => {
          setAssets(data)
          setError(null)
        })
        .catch((e: Error) => {
          if (e.name !== 'AbortError') setError(e.message)
        })
    }, 250)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [search, status, categoryId, locationId, openId])

  if (printing) return <LabelSheet assetIds={printing} onBack={() => setPrinting(null)} />
  if (openId) return <AssetDetailsPage assetId={openId} onBack={() => setOpenId(null)} />
  if (adding)
    return (
      <AddAssetForm
        onCreated={(id) => {
          setAdding(false)
          setOpenId(id)
        }}
        onOpen={(id) => {
          setAdding(false)
          setOpenId(id)
        }}
        onCancel={() => setAdding(false)}
      />
    )

  return (
    <>
      <PageHeader
        title="Assets"
        subtitle="Every tracked device, who has it and where it is in its lifecycle."
        actions={
          canChange && (
            <>
              <button disabled={selected.length === 0} onClick={() => setPrinting(selected)}>
                <Printer size={16} /> Print labels{selected.length > 0 && ` (${selected.length})`}
              </button>
              <button className="primary" onClick={() => setAdding(true)}>
                <Plus size={16} /> Add asset
              </button>
            </>
          )
        }
      />
      <div className="form-row filters">
        <input
          type="search"
          placeholder="Search or scan serial, asset tag, model or person (Enter opens an exact match)"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setNotFound(null)
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') lookup()
          }}
        />
        <select aria-label="Status" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">All statuses</option>
          {Object.entries(statusLabel).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select aria-label="Category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">All categories</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <LocationSelect value={locationId} onChange={setLocationId} allowNone noneLabel="All locations" />
        {(status || categoryId || locationId) && (
          <button
            className="link"
            onClick={() => {
              setStatus('')
              setCategoryId('')
              setLocationId('')
            }}
          >
            Clear filters
          </button>
        )}
      </div>
      {notFound && (
        <p className="muted small">
          No device with serial or tag “{notFound}”.{' '}
          <button className="link" onClick={() => setAdding(true)}>
            Add it as a new asset
          </button>
        </p>
      )}
      {error && <p className="muted">Could not load assets: {error}. Is Giim.Api running?</p>}
      <table>
        <thead>
          <tr>
            <th>
              <input
                type="checkbox"
                aria-label="Select all shown"
                checked={assets.length > 0 && assets.every((a) => selected.includes(a.id))}
                onChange={(e) =>
                  setSelected((s) =>
                    e.target.checked ? [...new Set([...s, ...assets.map((a) => a.id)])] : s.filter((id) => !assets.some((a) => a.id === id)),
                  )
                }
              />
            </th>
            <th>Asset tag</th>
            <th>Serial</th>
            <th>Make / model</th>
            <th>Type</th>
            <th>Status</th>
            <th>Assigned to</th>
            <th>Location</th>
          </tr>
        </thead>
        <tbody>
          {assets.map((a) => (
            <tr key={a.id} className="clickable" onClick={() => setOpenId(a.id)}>
              <td onClick={(e) => e.stopPropagation()}>
                <input type="checkbox" aria-label={`Select ${a.assetTag ?? a.serialNumber}`} checked={selected.includes(a.id)} onChange={() => toggle(a.id)} />
              </td>
              <td>
                <button className="link" onClick={() => setOpenId(a.id)}>
                  {a.assetTag ?? a.serialNumber}
                </button>
              </td>
              <td>{a.serialNumber}</td>
              <td>
                {a.manufacturer} {a.model}
              </td>
              <td>{a.category}</td>
              <td>
                <StatusBadge status={a.status} />
              </td>
              <td>
                {a.assignedTo ?? (a.legacyAssignedTo ? <span className="muted">{a.legacyAssignedTo} (unlinked)</span> : '-')}
              </td>
              <td>{a.location ?? '-'}</td>
            </tr>
          ))}
          {assets.length === 0 && !error && (
            <tr>
              <td colSpan={8} className="muted">
                No assets found.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </>
  )
}
