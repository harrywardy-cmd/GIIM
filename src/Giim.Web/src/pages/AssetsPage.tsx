import { useEffect, useState } from 'react'
import { api } from '../api'
import { StatusBadge } from '../StatusBadge'
import { AssetDetailsPage } from './AssetDetailsPage'

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

export function AssetsPage() {
  const [search, setSearch] = useState('')
  const [assets, setAssets] = useState<Asset[]>([])
  const [error, setError] = useState<string | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)

  useEffect(() => {
    if (openId) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      api<Asset[]>(`/api/assets?search=${encodeURIComponent(search)}`, { signal: controller.signal })
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
  }, [search, openId])

  if (openId) return <AssetDetailsPage assetId={openId} onBack={() => setOpenId(null)} />

  return (
    <>
      <h2>Assets</h2>
      <input
        type="search"
        placeholder="Search serial number or asset tag"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      {error && <p className="muted">Could not load assets: {error}. Is Giim.Api running?</p>}
      <table>
        <thead>
          <tr>
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
              <td colSpan={7} className="muted">
                No assets found.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </>
  )
}
