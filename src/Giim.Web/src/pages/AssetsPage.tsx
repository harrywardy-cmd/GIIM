import { useEffect, useState } from 'react'

type Asset = {
  id: string
  assetTag: string | null
  serialNumber: string
  manufacturer: string
  model: string
  category: string
  status: string
  location: string | null
}

export function AssetsPage() {
  const [search, setSearch] = useState('')
  const [assets, setAssets] = useState<Asset[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      fetch(`/api/assets?search=${encodeURIComponent(search)}`, { signal: controller.signal })
        .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`API returned ${r.status}`))))
        .then((data: Asset[]) => {
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
  }, [search])

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
            <th>Location</th>
          </tr>
        </thead>
        <tbody>
          {assets.map((a) => (
            <tr key={a.id}>
              <td>{a.assetTag ?? '-'}</td>
              <td>{a.serialNumber}</td>
              <td>{a.manufacturer} {a.model}</td>
              <td>{a.category}</td>
              <td>{a.status}</td>
              <td>{a.location ?? '-'}</td>
            </tr>
          ))}
          {assets.length === 0 && !error && (
            <tr>
              <td colSpan={6} className="muted">No assets yet. They will appear once the import is built.</td>
            </tr>
          )}
        </tbody>
      </table>
    </>
  )
}
