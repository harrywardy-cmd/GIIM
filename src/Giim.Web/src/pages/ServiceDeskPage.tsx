import { useEffect, useState } from 'react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'

type Status = {
  mode: 'None' | 'File' | 'Api'
  webhookEnabled: boolean
  webhookUrl: string | null
  site: string
  autoRaiseDeviceRequests: boolean
  resolveWhenComplete: boolean
  starter: { templates: string[]; fields: Record<string, string> }
  leaver: { templates: string[]; fields: Record<string, string> }
  inbound: { id: string; requestKey: string; displayId: string | null; kind: string; status: string; message: string | null; caseId: string | null; attempts: number; createdAt: string }[]
  updates: { id: string; displayId: string; kind: string; status: string; content: string; attempts: number; createdAt: string; sentAt: string | null; lastError: string | null }[]
}

const formatDateTime = (value: string) => new Date(value).toLocaleString('en-AU', { dateStyle: 'short', timeStyle: 'short' })
const modeText = { None: 'Not connected', File: 'Stand-in (sample tickets, notes to a log file)', Api: 'Connected to ServiceDesk Plus' }
const inboundText: Record<string, string> = {
  Pending: 'Waiting',
  Processed: 'Checklist created',
  Ignored: 'Ignored',
  NeedsAttention: 'Needs attention',
  Failed: 'Failed',
}

/** The ServiceDesk Plus connection: settings, tickets received and notes sent. Administrators only. */
export function ServiceDeskPage() {
  const nav = useNav()
  const [status, setStatus] = useState<Status | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    api<Status>('/api/integrations/servicedesk')
      .then((s) => {
        setStatus(s)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  if (!status) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  const s = status
  const attention = s.inbound.filter((e) => e.status === 'NeedsAttention' || e.status === 'Failed').length

  const retry = async (id: string) => {
    try {
      await api(`/api/integrations/servicedesk/inbound/${id}/retry`, { method: 'POST' })
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <>
      <PageHeader
        title="ServiceDesk Plus"
        subtitle="New-starter and leaver tickets become checklists automatically, and GIIM notes progress on the ticket."
        actions={<button onClick={() => setRefresh((n) => n + 1)}>Refresh</button>}
      />
      {error && <p className="error">{error}</p>}
      {attention > 0 && (
        <p className="notice">
          {attention} ticket{attention === 1 ? '' : 's'} need{attention === 1 ? 's' : ''} attention: fix the cause (e.g. add the department or person), then
          Retry.
        </p>
      )}

      <div className="details-grid">
        <section className="panel">
          <h3>Connection</h3>
          <dl className="facts">
            <dt>Status</dt>
            <dd>{modeText[s.mode]}</dd>
            <dt>Site</dt>
            <dd>{s.site}</dd>
            <dt>Webhook</dt>
            <dd>
              {s.webhookEnabled ? (s.webhookUrl ?? 'On (set Giim:PublicBaseUrl to show its address)') : 'Off'}
              {s.webhookEnabled && <div className="muted small">Header X-GIIM-Webhook-Secret; body {'{"requestId": "<ticket id>"}'}</div>}
            </dd>
            <dt>Device requests</dt>
            <dd>{s.autoRaiseDeviceRequests ? 'Raised automatically for a starter’s devices' : 'Raised by IT from the checklist'}</dd>
            <dt>Resolve tickets</dt>
            <dd>{s.resolveWhenComplete ? 'When the checklist is complete' : 'No (notes only)'}</dd>
          </dl>
        </section>
        <section className="panel">
          <h3>Ticket fields</h3>
          <dl className="facts">
            <dt>Starter templates</dt>
            <dd>{s.starter.templates.join(', ') || '-'}</dd>
            {Object.entries(s.starter.fields).map(([k, v]) => (
              <Field key={`s-${k}`} name={k} value={v} />
            ))}
            <dt>Leaver templates</dt>
            <dd>{s.leaver.templates.join(', ') || '-'}</dd>
            {Object.entries(s.leaver.fields).map(([k, v]) => (
              <Field key={`l-${k}`} name={k} value={v} />
            ))}
          </dl>
        </section>
      </div>

      <section className="panel">
        <h3>Tickets received</h3>
        <table>
          <thead>
            <tr>
              <th>Received</th>
              <th>Ticket</th>
              <th>Type</th>
              <th>Outcome</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {s.inbound.map((e) => (
              <tr key={e.id}>
                <td>{formatDateTime(e.createdAt)}</td>
                <td>{e.displayId ?? e.requestKey}</td>
                <td>{e.kind === 'Unknown' ? '-' : e.kind}</td>
                <td>
                  <span className={`status inbound-${e.status}`}>{inboundText[e.status] ?? e.status}</span>
                  {e.message && <div className="muted small">{e.message}</div>}
                </td>
                <td>
                  {e.caseId && (
                    <button className="link" onClick={() => nav.openCase(e.caseId!)}>
                      Open checklist
                    </button>
                  )}
                  {(e.status === 'NeedsAttention' || e.status === 'Failed') && (
                    <button className="link" onClick={() => retry(e.id)}>
                      Retry
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {s.inbound.length === 0 && (
              <tr>
                <td colSpan={5} className="muted">
                  No tickets received yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>

      <section className="panel">
        <h3>Notes sent to tickets</h3>
        <table>
          <thead>
            <tr>
              <th>Queued</th>
              <th>Ticket</th>
              <th>Note</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {s.updates.map((u) => (
              <tr key={u.id}>
                <td>{formatDateTime(u.createdAt)}</td>
                <td>{u.displayId}</td>
                <td className="small">
                  {u.kind === 'Resolve' && <strong>Resolve: </strong>}
                  {textOf(u.content)}
                </td>
                <td>
                  <span className={`status update-${u.status}`}>{u.status === 'Pending' && u.attempts > 0 ? 'Retrying' : u.status}</span>
                  {u.lastError && <div className="muted small">{u.lastError}</div>}
                </td>
              </tr>
            ))}
            {s.updates.length === 0 && (
              <tr>
                <td colSpan={4} className="muted">
                  No notes yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </>
  )
}

function Field({ name, value }: { name: string; value: string }) {
  return (
    <>
      <dt className="small">{name}</dt>
      <dd className="small">
        <code>{value}</code>
      </dd>
    </>
  )
}

/** Notes are HTML for ServiceDesk Plus; shown here as plain text (never inserted as HTML). */
const textOf = (html: string) => new DOMParser().parseFromString(html, 'text/html').body.textContent ?? ''
