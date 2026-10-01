import { useEffect, useState } from 'react'
import { api } from '../api'
import { useNav } from '../nav'
import { PageHeader } from '../PageHeader'

type Settings = {
  emailMode: string
  enabled: boolean
  itTeam: string[]
  managerEmails: boolean
  digestTime: string
  warrantyDay: string
  dueSoonDays: number
  warrantyDays: number
  approvalReminderAfterDays: number
  maxApprovalReminders: number
  returnReminderEveryDays: number
  maxReturnReminders: number
  timeZone: string
  runs: { name: string; lastRunOn: string; lastRunAt: string; lastResult: string | null }[]
  emails: {
    id: string
    kind: string
    toName: string | null
    toAddress: string
    subject: string
    status: string
    attempts: number
    createdAt: string
    sentAt: string | null
    lastError: string | null
    requestId: string | null
  }[]
}

const jobs = [
  { name: 'it-digest', label: 'Daily IT digest' },
  { name: 'warranty-list', label: 'Weekly warranty list' },
]

const kindText: Record<string, string> = {
  RequestApprovalNeeded: 'Approval needed',
  RequestApprovalReminder: 'Approval reminder',
  RequestInfoRequested: 'More info needed',
  RequestInfoProvided: 'Info provided',
  RequestApproved: 'Request approved',
  RequestRejected: 'Request rejected',
  RequestCompleted: 'Request handed over',
  ManagerStarter: 'New starter (manager)',
  ManagerLeaver: 'Leaver (manager)',
  ReturnReminder: 'Equipment not returned',
  ItDigest: 'IT digest',
  WarrantyList: 'Warranty list',
}

const formatDateTime = (value: string) => new Date(value).toLocaleString('en-AU', { dateStyle: 'short', timeStyle: 'short' })

/** Emails GIIM sends: settings, scheduled digests and every recent email with whether it went. Administrators only. */
export function NotificationsPage() {
  const nav = useNav()
  const [settings, setSettings] = useState<Settings | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<Settings>('/api/notifications')
      .then((s) => {
        setSettings(s)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [refresh])

  if (!settings) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>
  const s = settings
  const failed = s.emails.filter((e) => e.status === 'Failed').length

  const runNow = async (job: string) => {
    setBusy(true)
    setMessage(null)
    try {
      const result = await api<{ result: string }>(`/api/notifications/jobs/${job}/run`, { method: 'POST' })
      setMessage(`${result.result}. The workers send queued emails within a minute.`)
      setRefresh((n) => n + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeader
        title="Notifications"
        subtitle="Emails GIIM sends: request approvals, messages to starters' and leavers' managers, reminders and IT digests."
        actions={<button onClick={() => setRefresh((n) => n + 1)}>Refresh</button>}
      />
      {error && <p className="error">{error}</p>}
      {message && <p className="notice">{message}</p>}
      {s.emailMode === 'None' && (
        <p className="notice">Sending is off (Email:Mode is None): emails wait in the queue and expire after 3 days. See docs/email-notifications.md.</p>
      )}
      {failed > 0 && <p className="error">{failed} recent email{failed === 1 ? '' : 's'} could not be sent; the reason is shown below.</p>}

      <div className="details-grid">
        <section className="panel">
          <h3>Settings</h3>
          <dl className="facts">
            <dt>Sending</dt>
            <dd>{s.emailMode === 'Graph' ? 'Microsoft 365' : s.emailMode === 'File' ? 'Files on this computer (development)' : 'Off'}</dd>
            <dt>Reminders</dt>
            <dd>{s.enabled ? 'On' : 'Off'}</dd>
            <dt>IT team</dt>
            <dd>{s.itTeam.length > 0 ? s.itTeam.join(', ') : <span className="error">Not set (Reminders:ItTeamAddresses)</span>}</dd>
            <dt>Managers</dt>
            <dd>{s.managerEmails ? 'Emailed when a starter or leaver checklist is created' : 'Not emailed'}</dd>
            <dt>Approvers</dt>
            <dd>
              Reminded after {s.approvalReminderAfterDays} days waiting, then every {s.approvalReminderAfterDays} days (at most {s.maxApprovalReminders})
            </dd>
            <dt>Unreturned equipment</dt>
            <dd>
              Leaver&apos;s manager reminded the day after the last day, then every {s.returnReminderEveryDays} days (at most {s.maxReturnReminders})
            </dd>
            <dt>IT digest</dt>
            <dd>
              Daily at {s.digestTime} ({s.timeZone}), only when something needs attention; starters and leavers within {s.dueSoonDays} days
            </dd>
            <dt>Warranty list</dt>
            <dd>
              {s.warrantyDay}s at {s.digestTime}: warranties ending within {s.warrantyDays} days
            </dd>
          </dl>
        </section>

        <section className="panel">
          <h3>Scheduled</h3>
          <table>
            <thead>
              <tr>
                <th>Job</th>
                <th>Last run</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((j) => {
                const run = s.runs.find((r) => r.name === j.name)
                return (
                  <tr key={j.name}>
                    <td>{j.label}</td>
                    <td>
                      {run ? formatDateTime(run.lastRunAt) : 'Not yet'}
                      {run?.lastResult && <div className="muted small">{run.lastResult}</div>}
                    </td>
                    <td>
                      <button disabled={busy} onClick={() => runNow(j.name)}>
                        Send now
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </section>
      </div>

      <section className="panel">
        <h3>Recent emails</h3>
        <table>
          <thead>
            <tr>
              <th>Queued</th>
              <th>Type</th>
              <th>To</th>
              <th>Subject</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {s.emails.map((e) => (
              <tr key={e.id}>
                <td>{formatDateTime(e.createdAt)}</td>
                <td>{kindText[e.kind] ?? e.kind}</td>
                <td>
                  {e.toName ?? e.toAddress}
                  {e.toName && <div className="muted small">{e.toAddress}</div>}
                </td>
                <td className="small">
                  {e.requestId ? (
                    <button className="link" onClick={() => nav.openRequest(e.requestId!)}>
                      {e.subject}
                    </button>
                  ) : (
                    e.subject
                  )}
                </td>
                <td>
                  <span className={`status update-${e.status}`}>
                    {e.status === 'Pending' && e.attempts > 0 ? 'Retrying' : e.status === 'Pending' ? 'Queued' : e.status}
                  </span>
                  {e.sentAt && <div className="muted small">{formatDateTime(e.sentAt)}</div>}
                  {e.lastError && <div className="muted small">{e.lastError}</div>}
                </td>
              </tr>
            ))}
            {s.emails.length === 0 && (
              <tr>
                <td colSpan={5} className="muted">
                  No emails yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </>
  )
}
