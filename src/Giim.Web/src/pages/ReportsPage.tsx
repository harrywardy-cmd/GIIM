import { useEffect, useState } from 'react'
import { Download } from 'lucide-react'
import { api } from '../api'
import { LocationSelect } from '../LocationSelect'
import { PageHeader } from '../PageHeader'
import { statusLabel } from '../status'

type ReportInfo = { key: string; title: string; description: string; usesDates: boolean }
type ColumnType = 'Text' | 'Number' | 'Money' | 'Date' | 'DateTime'

type ReportData = {
  key: string
  title: string
  description: string
  figures: { label: string; value: string }[]
  columns: { key: string; label: string; type: ColumnType }[]
  rows: Record<string, string | number | null>[]
  totalRows: number
  chartTitle: string | null
  chart: { label: string; value: number }[] | null
  periodFrom: string
  periodTo: string
}

const australian = new Intl.NumberFormat('en-AU')
const money = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' })

/** "2026-09-29" -> "29/09/2026" without time-zone surprises. */
const date = (iso: string) => iso.slice(0, 10).split('-').reverse().join('/')

function cell(value: string | number | null, type: ColumnType) {
  if (value === null || value === '') return ''
  switch (type) {
    case 'Money':
      return money.format(Number(value))
    case 'Number':
      return australian.format(Number(value))
    case 'Date':
      return date(String(value))
    case 'DateTime':
      return new Date(String(value)).toLocaleString('en-AU', { dateStyle: 'short', timeStyle: 'short' })
    default:
      return String(value)
  }
}

const numeric = (type: ColumnType) => (type === 'Money' || type === 'Number' ? 'num' : '')

export function ReportsPage() {
  const [catalogue, setCatalogue] = useState<ReportInfo[]>([])
  const [key, setKey] = useState('inventory')
  const [categories, setCategories] = useState<{ id: string; name: string }[]>([])
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [status, setStatus] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [locationId, setLocationId] = useState('')
  const [days, setDays] = useState('90')
  const [result, setResult] = useState<{ request: string; report: ReportData } | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([api<ReportInfo[]>('/api/reports'), api<{ id: string; name: string }[]>('/api/categories')])
      .then(([c, cats]) => {
        setCatalogue(c)
        setCategories(cats)
      })
      .catch((e: Error) => setError(e.message))
  }, [])

  const usesDates = catalogue.find((r) => r.key === key)?.usesDates ?? false
  const params = new URLSearchParams()
  if (usesDates) {
    if (from) params.set('from', from)
    if (to) params.set('to', to)
  }
  if (key === 'inventory') {
    if (status) params.set('status', status)
    if (categoryId) params.set('categoryId', categoryId)
    if (locationId) params.set('locationId', locationId)
  }
  if (key === 'warranty' && Number(days) > 0) params.set('days', days)
  const query = params.toString()
  const request = `/api/reports/${key}${query ? `?${query}` : ''}`

  useEffect(() => {
    let current = true
    api<ReportData>(request)
      .then((report) => {
        if (!current) return
        setResult({ request, report })
        setError(null)
        // Fill in the period the server used, so empty date boxes still say what was reported on.
        if (usesDates) {
          setFrom((f) => f || report.periodFrom)
          setTo((t) => t || report.periodTo)
        }
      })
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [request, usesDates])

  const shown = result?.report.key === key ? result.report : null
  const loading = result?.request !== request
  const exportUrl = (format: 'csv' | 'xlsx') => `/api/reports/${key}/export?format=${format}${query ? `&${query}` : ''}`
  const chartMax = Math.max(1, ...(shown?.chart ?? []).map((b) => b.value))

  return (
    <>
      <PageHeader
        title="Reports"
        subtitle="Figures for audits, budgets and follow-up. Exports always include every row."
        actions={
          <>
            <a className="button" href={exportUrl('csv')} download>
              <Download size={16} /> CSV
            </a>
            <a className="button primary" href={exportUrl('xlsx')} download>
              <Download size={16} /> Excel
            </a>
          </>
        }
      />

      <div className="tabs" role="tablist">
        {catalogue.map((r) => (
          <button
            key={r.key}
            role="tab"
            aria-selected={r.key === key}
            className={r.key === key ? 'selected' : ''}
            title={r.description}
            onClick={() => setKey(r.key)}
          >
            {r.title}
          </button>
        ))}
      </div>

      {(usesDates || key === 'inventory' || key === 'warranty') && (
        <div className="form-row filters">
          {usesDates && (
            <>
              <label>
                From
                <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
              </label>
              <label>
                To
                <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
              </label>
            </>
          )}
          {key === 'inventory' && (
            <>
              <select aria-label="Status" value={status} onChange={(e) => setStatus(e.target.value)}>
                <option value="">All except disposed</option>
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
            </>
          )}
          {key === 'warranty' && (
            <label>
              Expiring within (days)
              <input type="number" min={1} max={3650} value={days} onChange={(e) => setDays(e.target.value)} style={{ width: 110 }} />
            </label>
          )}
        </div>
      )}

      {error && <p className="error">{error}</p>}
      {!shown && !error && <p className="muted">Loading…</p>}

      {shown && (
        <div style={{ opacity: loading ? 0.6 : 1 }} aria-busy={loading}>
          <p className="muted">{shown.description}</p>

          <div className="stats">
            {shown.figures.map((f) => (
              <div key={f.label} className="stat">
                <div className="stat-value">{f.value}</div>
                <div className="muted">{f.label}</div>
              </div>
            ))}
          </div>

          {shown.chart && shown.chart.length > 0 && (
            <section className="card">
              <div className="card-header">
                <h3>{shown.chartTitle}</h3>
              </div>
              <ul className="hbars">
                {shown.chart.map((b) => (
                  <li key={b.label}>
                    <span className="hbar-label" title={b.label}>
                      {b.label}
                    </span>
                    <span className="hbar-track">
                      <span className="hbar" style={{ width: `${(b.value / chartMax) * 100}%` }} />
                    </span>
                    <span className="hbar-value">{australian.format(b.value)}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {shown.totalRows > shown.rows.length && (
            <p className="muted small">
              Showing the first {australian.format(shown.rows.length)} of {australian.format(shown.totalRows)} rows. Export to get them all.
            </p>
          )}
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  {shown.columns.map((c) => (
                    <th key={c.key} className={numeric(c.type)}>
                      {c.label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {shown.rows.map((row, i) => (
                  <tr key={i}>
                    {shown.columns.map((c) => (
                      <td key={c.key} className={numeric(c.type)}>
                        {cell(row[c.key], c.type)}
                      </td>
                    ))}
                  </tr>
                ))}
                {shown.rows.length === 0 && (
                  <tr>
                    <td colSpan={shown.columns.length} className="muted">
                      Nothing to report for these settings.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  )
}
