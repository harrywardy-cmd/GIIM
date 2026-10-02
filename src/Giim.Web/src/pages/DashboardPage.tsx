import { useEffect, useState, type ReactNode } from 'react'
import { ArrowDown, ArrowUp, CircleCheck, Clock, Laptop, PackageCheck, PackageX, ShieldAlert, TriangleAlert, UserX, Wrench } from 'lucide-react'
import { api } from '../api'
import { useNav } from '../nav'
import type { PageKey } from '../App'
import { PageHeader } from '../PageHeader'

type Totals = { inService: number; assigned: number; available: number; inRepair: number; retired: number; disposed: number; pendingApproval: number }

type Dashboard = {
  totals: Totals
  /** The same totals on an earlier day (about a week ago), or null until there is history. */
  trend: { since: string; then: Totals } | null
  statusBreakdown: { key: string; label: string; count: number }[]
  warrantyExpiring: { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; category: string; warrantyExpiry: string; daysLeft: number }[]
  attention: {
    leaversWithKit: number
    returnsRequested: number
    awaitingWipe: number
    openRepairs: number
    unlinkedOwners: number
    notSeenInIntune: number
    warrantyExpired: number
    lowStock: number
  }
  lastIntuneSync: string | null
  recentActivity: {
    id: number
    occurredAt: string
    type: string
    summary: string
    actor: string
    ticketNumber: string | null
    assetId: string
    assetTag: string | null
    serialNumber: string
    manufacturer: string
    model: string
  }[]
}

// Fixed category order -> fixed colour slot, so a category never changes colour (palette validated for CVD).
const seriesColour: Record<string, string> = {
  Assigned: 'var(--series-1)',
  Available: 'var(--series-3)',
  Processing: 'var(--series-4)',
  InRepair: 'var(--series-5)',
  Missing: 'var(--series-2)',
}

const number = (n: number) => n.toLocaleString('en-AU')
const percent = (n: number, total: number) => (total ? `${((n / total) * 100).toFixed(n / total < 0.01 && n > 0 ? 1 : 0)}%` : '0%')

export function DashboardPage({ onOpenAsset, onNavigate }: { onOpenAsset: (id: string) => void; onNavigate: (page: PageKey) => void }) {
  const [data, setData] = useState<Dashboard | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [hovered, setHovered] = useState<string | null>(null)
  const nav = useNav()

  useEffect(() => {
    api<Dashboard>('/api/dashboard')
      .then(setData)
      .catch((e: Error) => setError(e.message))
  }, [])

  if (!data) return error ? <p className="error">Could not load the dashboard: {error}</p> : <p className="muted">Loading…</p>

  const { totals, attention } = data
  const was = (key: keyof Totals) => (data.trend ? { then: data.trend.then[key], since: data.trend.since } : undefined)
  const breakdownTotal = data.statusBreakdown.reduce((sum, b) => sum + b.count, 0)
  const focus = data.statusBreakdown.find((b) => b.key === hovered)

  const attentionItems: { label: string; count: number; icon: ReactNode; tone: string; go: PageKey }[] = [
    { label: 'People who have left but still hold kit', count: attention.leaversWithKit, icon: <UserX size={16} />, tone: 'tone-rose', go: 'people' },
    { label: 'Returned devices waiting to be wiped', count: attention.awaitingWipe, icon: <ShieldAlert size={16} />, tone: 'tone-amber', go: 'assets' },
    { label: 'Assigned devices not seen in Intune for 90+ days', count: attention.notSeenInIntune, icon: <TriangleAlert size={16} />, tone: 'tone-amber', go: 'reconciliation' },
    { label: 'Spreadsheet owners still to link', count: attention.unlinkedOwners, icon: <UserX size={16} />, tone: 'tone-grey', go: 'people' },
    { label: 'Open repairs', count: attention.openRepairs, icon: <Wrench size={16} />, tone: 'tone-violet', go: 'assets' },
    { label: 'Stock items at or below reorder level', count: attention.lowStock, icon: <PackageX size={16} />, tone: 'tone-rose', go: 'stock' },
    { label: 'Devices in service with expired warranty', count: attention.warrantyExpired, icon: <ShieldAlert size={16} />, tone: 'tone-grey', go: 'assets' },
  ]

  return (
    <>
      <PageHeader title="Dashboard" subtitle="Overview of IT assets, what needs attention, and recent activity." />

      <div className="stats">
        <Tile label="Assets in service" value={totals.inService} was={was('inService')} icon={<Laptop size={18} />} tone="tone-violet" />
        <Tile label="Assigned" value={totals.assigned} was={was('assigned')} icon={<CircleCheck size={18} />} tone="tone-green" />
        <Tile label="Ready to deploy" value={totals.available} was={was('available')} icon={<PackageCheck size={18} />} tone="tone-blue" />
        <Tile label="Requests pending approval" value={totals.pendingApproval} was={was('pendingApproval')} icon={<Clock size={18} />} tone="tone-amber" />
        <Tile label="In repair" value={totals.inRepair} was={was('inRepair')} icon={<Wrench size={18} />} tone="tone-rose" />
        <Tile label="Retired" value={totals.retired} was={was('retired')} icon={<PackageX size={18} />} tone="tone-grey" note={`${number(totals.disposed)} disposed`} />
      </div>

      <div className="dash-grid">
        <section className="card">
          <div className="card-header">
            <h3>Asset status</h3>
            <span className="muted small">{number(breakdownTotal)} in service</span>
          </div>
          <p className="small muted" style={{ margin: 0, minHeight: 20 }} aria-live="polite">
            {focus ? `${focus.label}: ${number(focus.count)} (${percent(focus.count, breakdownTotal)})` : 'Hover a segment for detail'}
          </p>
          <div
            className="stackbar"
            role="img"
            aria-label={data.statusBreakdown.map((b) => `${b.label} ${b.count}`).join(', ')}
            onMouseLeave={() => setHovered(null)}
          >
            {data.statusBreakdown
              .filter((b) => b.count > 0)
              .map((b) => (
                <div
                  key={b.key}
                  style={{
                    flexGrow: b.count,
                    background: seriesColour[b.key],
                    opacity: hovered && hovered !== b.key ? 0.35 : 1,
                  }}
                  onMouseEnter={() => setHovered(b.key)}
                />
              ))}
          </div>
          {/* Legend doubles as the data table: label, count and share, so nothing relies on colour alone. */}
          <ul className="legend">
            {data.statusBreakdown.map((b) => (
              <li key={b.key} onMouseEnter={() => setHovered(b.key)} onMouseLeave={() => setHovered(null)}>
                <span className="swatch" style={{ background: seriesColour[b.key] }} />
                <span>{b.label}</span>
                <span className="value">{number(b.count)}</span>
                <span className="pct">{percent(b.count, breakdownTotal)}</span>
              </li>
            ))}
          </ul>
        </section>

        <section className="card">
          <div className="card-header">
            <h3>Needs attention</h3>
          </div>
          <ul className="attention">
            {attentionItems.map((a) => (
              <li key={a.label}>
                <span className={`stat-icon ${a.tone}`} style={{ position: 'static', width: 30, height: 30 }}>
                  {a.icon}
                </span>
                <button className="link" onClick={() => onNavigate(a.go)}>
                  {a.label}
                </button>
                <span className="count">{number(a.count)}</span>
              </li>
            ))}
          </ul>
        </section>

        <section className="card">
          <div className="card-header">
            <h3>Warranty expiring (90 days)</h3>
          </div>
          {data.warrantyExpiring.length === 0 ? (
            <p className="muted small">Nothing expiring in the next 90 days.</p>
          ) : (
            <ul className="warranty-list">
              {data.warrantyExpiring.map((w) => (
                <li key={w.id}>
                  <Laptop size={18} className="muted" />
                  <div>
                    <button className="link" onClick={() => onOpenAsset(w.id)}>
                      {w.manufacturer} {w.model}
                    </button>
                    <div className="muted small">
                      {w.assetTag ?? w.serialNumber} · {w.category}
                    </div>
                  </div>
                  <span className={`days ${w.daysLeft <= 14 ? 'soon' : ''}`}>
                    {w.daysLeft === 0 ? 'today' : `${w.daysLeft} day${w.daysLeft === 1 ? '' : 's'}`}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="card">
          <div className="card-header">
            <h3>Data sources</h3>
          </div>
          <dl className="facts">
            <dt>Intune last synced</dt>
            <dd>{data.lastIntuneSync ? new Date(data.lastIntuneSync).toLocaleString('en-AU') : 'Never'}</dd>
            <dt>Retired / disposed</dt>
            <dd>
              {number(totals.retired)} / {number(totals.disposed)}
            </dd>
          </dl>
        </section>

        <section className="card dash-full">
          <div className="card-header">
            <h3>Recent activity</h3>
          </div>
          <table>
            <thead>
              <tr>
                <th>When</th>
                <th>What happened</th>
                <th>Asset</th>
                <th>Technician</th>
                <th>Ticket</th>
              </tr>
            </thead>
            <tbody>
              {data.recentActivity.map((e) => (
                <tr key={e.id} className="clickable" onClick={() => onOpenAsset(e.assetId)}>
                  <td className="small">{new Date(e.occurredAt).toLocaleString('en-AU', { dateStyle: 'medium', timeStyle: 'short' })}</td>
                  <td>
                    <span className="dot" />
                    {e.summary}
                  </td>
                  <td>
                    {e.manufacturer} {e.model}
                    <div className="muted small">{e.assetTag ?? e.serialNumber}</div>
                  </td>
                  <td onClick={(event) => event.stopPropagation()}>
                    <button className="link" onClick={() => nav.openTechnician(e.actor)}>
                      {e.actor}
                    </button>
                  </td>
                  <td onClick={(event) => event.stopPropagation()}>
                    {e.ticketNumber ? (
                      <button className="link" onClick={() => nav.openTicket(e.ticketNumber!)}>
                        {e.ticketNumber}
                      </button>
                    ) : (
                      '-'
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>
    </>
  )
}

function Tile({ label, value, was, icon, tone, note }: {
  label: string
  value: number
  /** The value on an earlier day, for the arrow (neutral colour: up isn't always good). */
  was?: { then: number; since: string }
  icon: ReactNode
  tone: string
  note?: string
}) {
  const change = was ? value - was.then : 0
  const since = was ? new Date(`${was.since}T00:00`).toLocaleDateString('en-AU', { weekday: 'short', day: 'numeric', month: 'short' }) : ''
  return (
    <div className="stat">
      <span className="muted small">{label}</span>
      <span className="stat-value">{number(value)}</span>
      {was && (
        <span className="stat-trend small" title={`${number(was.then)} on ${since}`}>
          {change > 0 ? <ArrowUp size={12} /> : change < 0 ? <ArrowDown size={12} /> : null}
          {change === 0 ? `no change since ${since}` : `${number(Math.abs(change))} since ${since}`}
        </span>
      )}
      {note && <span className="muted small">{note}</span>}
      <span className={`stat-icon ${tone}`}>{icon}</span>
    </div>
  )
}
