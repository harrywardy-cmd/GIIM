import { useEffect, useState } from 'react'
import { api } from '../api'
import type { AutomationOverview } from '../automation'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'

const formatDateTime = (value: string) => new Date(value).toLocaleString('en-AU', { dateStyle: 'short', timeStyle: 'short' })
const stepText: Record<string, string> = {
  CreateAccount: 'Create AD account',
  WaitForCloudSync: 'Wait for Entra sync',
  EnableRemoteMailbox: 'Enable remote mailbox',
  AddToGroup: 'Add to group',
  EnableAccount: 'Enable account',
  SendWelcomeEmail: 'Welcome email',
  AddToCloudGroup: 'Add to Entra group',
}

/** The on-prem agent and starter automation: connected agents, the queue, recent failures. Administrators. */
export function AutomationPage() {
  const nav = useNav()
  const [data, setData] = useState<AutomationOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    api<AutomationOverview>('/api/automation')
      .then((d) => {
        setData(d)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  if (!data) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  const online = data.agents.filter((a) => a.online)

  return (
    <>
      <PageHeader
        title="Automation"
        subtitle="The on-prem agent creates starters' AD accounts, mailboxes and group memberships; GIIM waits for the cloud sync and emails the manager."
        actions={<button onClick={() => setRefresh((n) => n + 1)}>Refresh</button>}
      />
      {error && <p className="error">{error}</p>}
      {data.dryRun && (
        <p className="notice">
          <strong>Dry run is on:</strong> automated steps only report what they would do, and the checklist tasks go back to a person. Turn it off
          (setting <code>Automation:DryRun</code>, or <code>automationDryRun</code> in Azure) once the AD administrators are happy with what the
          dry runs show.
        </p>
      )}
      {online.length === 0 && (
        <p className="notice">
          No agent is connected, so AD and Exchange steps wait. See docs/onprem-agent.md to install one, or start the stand-in agent on a developer PC.
        </p>
      )}

      <div className="details-grid">
        <section className="panel">
          <h3>Agents</h3>
          {data.agents.length === 0 ? (
            <p className="muted small">No agent has ever connected.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Agent</th>
                  <th>Last seen</th>
                  <th>Directory</th>
                  <th>Version</th>
                </tr>
              </thead>
              <tbody>
                {data.agents.map((a) => (
                  <tr key={a.name}>
                    <td>
                      <strong>{a.name}</strong>
                      <div className={`small ${a.online ? 'success' : 'error'}`}>{a.online ? 'connected' : 'offline'}</div>
                      {a.dryRun && <span className="tag tag-warn">agent dry run</span>}
                    </td>
                    <td className="small">{formatDateTime(a.lastSeenAt)}</td>
                    <td className="small">{a.directory ?? '-'}</td>
                    <td className="small">{a.version ?? '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h3>Queue</h3>
          <dl className="facts">
            <dt>Ready to run</dt>
            <dd>{data.queued}</dd>
            <dt>Running now</dt>
            <dd>{data.running}</dd>
            <dt>Waiting for a date</dt>
            <dd>{data.waiting} (e.g. enabling an account on its start date)</dd>
          </dl>
        </section>

        <section className="panel">
          <h3>Failed in the last 14 days ({data.recentFailures.length})</h3>
          {data.recentFailures.length === 0 ? (
            <p className="muted small">Nothing has failed.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Starter</th>
                  <th>Step</th>
                  <th>Why</th>
                </tr>
              </thead>
              <tbody>
                {data.recentFailures.map((f) => (
                  <tr key={f.jobId}>
                    <td>
                      <button className="link" onClick={() => nav.openCase(f.caseId)}>
                        {f.person}
                      </button>
                      {f.at && <div className="muted small">{formatDateTime(f.at)}</div>}
                    </td>
                    <td className="small">{stepText[f.step] ?? f.step}</td>
                    <td className="small">{f.error}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="muted small">Fix the cause (e.g. create the missing group), then Retry the step on the starter&apos;s checklist, or stop it and do it by hand.</p>
        </section>
      </div>
    </>
  )
}
