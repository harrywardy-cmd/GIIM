import { useEffect, useState } from 'react'
import { api } from '../api'
import { PageHeader } from '../PageHeader'
import { statusText } from '../status'

type Finding = 'IntuneOnly' | 'NotInIntune' | 'Stale' | 'OwnerMismatch' | 'StatusConflict'

type SyncRun = {
  startedAt: string
  completedAt: string | null
  status: 'Running' | 'Succeeded' | 'Failed'
  devicesSeen: number
  added: number
  removed: number
  error: string | null
}

type Row = {
  serialNumber: string | null
  findings: Finding[]
  assetTag: string | null
  category: string | null
  status: string | null
  registerOwner: string | null
  intuneDeviceName: string | null
  intuneModel: string | null
  intuneUser: string | null
  intuneLastSync: string | null
}

type Report = {
  lastSync: SyncRun | null
  registerAssets: number
  intuneDevices: number
  clean: number
  findingCounts: Partial<Record<Finding, number>>
  filteredCount: number
  rows: Row[]
}

const findings: { key: Finding; label: string; help: string }[] = [
  { key: 'StatusConflict', label: 'Still in use', help: 'Register says returned / spare / lost, but Intune shows someone using it in the last 30 days.' },
  { key: 'Stale', label: 'Not seen 90+ days', help: 'In Intune but not checked in for over 90 days. Often a leaver’s kit that never came back.' },
  { key: 'NotInIntune', label: 'Missing from Intune', help: 'An in-use laptop, desktop, phone or tablet that Intune doesn’t know about.' },
  { key: 'OwnerMismatch', label: 'Different user', help: 'Intune’s user doesn’t match who the register says has it.' },
  { key: 'IntuneOnly', label: 'Not in register', help: 'Enrolled in Intune but not recorded in the register.' },
]

const PAGE_SIZE = 100

export function ReconciliationPage() {
  const [report, setReport] = useState<Report | null>(null)
  const [finding, setFinding] = useState<Finding | ''>('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [refresh, setRefresh] = useState(0)
  const [syncing, setSyncing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (finding) params.set('finding', finding)
    if (search.trim()) params.set('search', search.trim())

    const timer = setTimeout(() => {
      api<Report>(`/api/reconciliation?${params}`, { signal: controller.signal })
        .then((r) => {
          setReport(r)
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
  }, [finding, search, page, refresh])

  const sync = async () => {
    setSyncing(true)
    setError(null)
    try {
      await api('/api/intune/sync', { method: 'POST' })
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSyncing(false)
    }
  }

  const choose = (f: Finding | '') => {
    setFinding(f)
    setPage(1)
  }

  const pages = report ? Math.max(1, Math.ceil(report.filteredCount / PAGE_SIZE)) : 1
  const last = report?.lastSync

  return (
    <>
      <PageHeader
        title="Intune reconciliation"
        subtitle="Compares the register with Intune by serial number. Read-only: nothing is changed in Intune."
      />

      <div className="sync-bar">
        <span className={last?.status === 'Failed' ? 'error' : 'muted'}>
          {!last
            ? 'Intune has not been synced yet.'
            : last.status === 'Failed'
              ? `Last sync failed ${new Date(last.startedAt).toLocaleString('en-AU')}: ${last.error}`
              : `Last synced ${new Date(last.completedAt ?? last.startedAt).toLocaleString('en-AU')} · ${last.devicesSeen.toLocaleString()} devices`}
        </span>
        <button onClick={sync} disabled={syncing}>
          {syncing ? 'Syncing…' : 'Sync now'}
        </button>
      </div>
      {error && <p className="error">{error}</p>}

      {report && (
        <>
          <div className="stats">
            <Stat label="Assets in register" value={report.registerAssets} />
            <Stat label="Devices in Intune" value={report.intuneDevices} />
            <Stat label="All good" value={report.clean} />
          </div>

          <div className="stats">
            <button className={`stat stat-button ${finding === '' ? 'selected' : ''}`} onClick={() => choose('')}>
              <div className="stat-value">{Object.values(report.findingCounts).reduce((a, b) => a + (b ?? 0), 0).toLocaleString()}</div>
              <div className="muted">All findings</div>
            </button>
            {findings.map((f) => (
              <button
                key={f.key}
                title={f.help}
                className={`stat stat-button ${finding === f.key ? 'selected' : ''} ${(report.findingCounts[f.key] ?? 0) > 0 && f.key !== 'IntuneOnly' ? 'stat-warn' : ''}`}
                onClick={() => choose(f.key)}
              >
                <div className="stat-value">{(report.findingCounts[f.key] ?? 0).toLocaleString()}</div>
                <div className="muted">{f.label}</div>
              </button>
            ))}
          </div>

          {finding && <p className="muted">{findings.find((f) => f.key === finding)?.help}</p>}

          <input
            type="search"
            placeholder="Search serial, asset tag, user or device name"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />

          <table>
            <thead>
              <tr>
                <th>Serial</th>
                <th>Finding</th>
                <th>Register</th>
                <th>Intune</th>
                <th>Last check-in</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r, i) => (
                <tr key={`${r.serialNumber}-${i}`}>
                  <td>
                    {r.serialNumber ?? <span className="muted">no serial</span>}
                    {r.assetTag && <div className="muted small">{r.assetTag}</div>}
                  </td>
                  <td className="small">{r.findings.map((f) => findings.find((x) => x.key === f)?.label ?? f).join(', ')}</td>
                  <td className="small">
                    {r.category ? (
                      <>
                        {r.category} · {statusText(r.status)}
                        <div className="muted">{r.registerOwner ?? 'no owner recorded'}</div>
                      </>
                    ) : (
                      <span className="muted">not in register</span>
                    )}
                  </td>
                  <td className="small">
                    {r.intuneDeviceName ? (
                      <>
                        {r.intuneDeviceName} · {r.intuneModel}
                        <div className="muted">{r.intuneUser ?? 'no user'}</div>
                      </>
                    ) : (
                      <span className="muted">not in Intune</span>
                    )}
                  </td>
                  <td className="small">{r.intuneLastSync ? new Date(r.intuneLastSync).toLocaleDateString('en-AU') : '-'}</td>
                </tr>
              ))}
              {report.rows.length === 0 && (
                <tr>
                  <td colSpan={5} className="muted">
                    Nothing to show.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div className="pager">
            <button disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Previous
            </button>
            <span className="muted">
              Page {page} of {pages} · {report.filteredCount.toLocaleString()} rows
            </span>
            <button disabled={page >= pages} onClick={() => setPage(page + 1)}>
              Next
            </button>
          </div>
        </>
      )}
    </>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat">
      <div className="stat-value">{value.toLocaleString()}</div>
      <div className="muted">{label}</div>
    </div>
  )
}
