import { useEffect, useState } from 'react'
import { Bot, Play, RotateCcw, Square } from 'lucide-react'
import { api } from '../api'
import { useUser } from '../user'
import { type CaseAutomation, type AutomationStepView, stepStatusText } from '../automation'

/**
 * A starter checklist's automation: whether an agent is connected, what would run (a preview before starting), and
 * each step's progress, with retry and stop. Refreshes itself while steps are running.
 */
export function AutomationPanel({ caseId, refreshKey, onChanged }: { caseId: string; refreshKey: number; onChanged: () => void }) {
  const { canChange } = useUser()
  const [data, setData] = useState<CaseAutomation | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)

  const active = data?.steps.some((s) => s.status === 'Queued' || s.status === 'Running') ?? false

  useEffect(() => {
    api<CaseAutomation>(`/api/cases/${caseId}/automation`)
      .then((d) => {
        setData(d)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [caseId, refreshKey, reload])

  // While steps run, look again every 10 seconds, and refresh the checklist as tasks tick off.
  useEffect(() => {
    if (!active) return
    // onChanged refreshes the page, which passes a new refreshKey, which reloads this panel.
    const timer = setInterval(onChanged, 10_000)
    return () => clearInterval(timer)
  }, [active, onChanged])

  if (!data) return error ? <p className="error small">{error}</p> : null

  const act = async (path: string) => {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/cases/${caseId}/automation/${path}`, { method: 'POST' })
      setConfirming(false)
      setReload((n) => n + 1)
      onChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }
  const ready = data.steps.filter((s) => !s.status || s.status === 'Failed' || s.status === 'Cancelled' || (s.status === 'Succeeded' && s.dryRun))
  const online = data.agents.find((a) => a.online)

  return (
    <section className="panel automation">
      <div className="section-title">
        <h3>
          <Bot size={18} /> Automation
        </h3>
        {data.dryRun && (
          <span className="tag tag-warn" title="Steps only report what they would do. An administrator turns this off once the AD rules are agreed.">
            dry run
          </span>
        )}
      </div>
      <p className="small muted">
        {online ? (
          <>
            Agent <strong>{online.name}</strong> is connected ({online.directory ?? 'directory'}).
          </>
        ) : (
          <span className="error">No on-prem agent is connected: AD and Exchange steps will wait until one is.</span>
        )}
      </p>

      {canChange && data.readyToStart > 0 && !confirming && (
        <button className="primary" onClick={() => setConfirming(true)}>
          <Play size={16} /> Run {data.readyToStart} automated step{data.readyToStart === 1 ? '' : 's'}
        </button>
      )}
      {data.cannotStart && data.readyToStart === 0 && !active && <p className="small muted">{data.cannotStart}</p>}

      {confirming && (
        <div className="action-form">
          <h4>{data.dryRun ? 'This is a dry run: nothing will be changed. It will:' : 'This will:'}</h4>
          <ol className="small automation-preview">
            {ready.map((s) => (
              <li key={s.taskId}>{s.description}</li>
            ))}
          </ol>
          <p className="small muted">
            GIIM never sees a password: the account is created disabled with a random one, and enabled on the start date.
          </p>
          <div className="form-row">
            <button className="primary" disabled={busy} onClick={() => act('start')}>
              {busy ? 'Starting…' : data.dryRun ? 'Start dry run' : 'Start'}
            </button>
            <button onClick={() => setConfirming(false)}>Cancel</button>
          </div>
        </div>
      )}
      {error && <p className="error small">{error}</p>}

      {data.steps.some((s) => s.status) && (
        <ul className="automation-steps">
          {data.steps.map((s) => (
            <Step key={s.taskId} step={s} canChange={canChange} busy={busy} onAct={act} />
          ))}
        </ul>
      )}
    </section>
  )
}

function Step({ step: s, canChange, busy, onAct }: { step: AutomationStepView; canChange: boolean; busy: boolean; onAct: (path: string) => void }) {
  const status = stepStatusText(s)
  return (
    <li>
      <div className="automation-step-head">
        <span className={`tag step-${s.status ?? 'none'}${s.dryRun && s.status === 'Succeeded' ? ' dry' : ''}`}>{status}</span>
        <span className="small">{s.task}</span>
      </div>
      {(s.error || s.log) && <div className={`small ${s.error && s.status === 'Failed' ? 'error' : 'muted'}`}>{s.status === 'Failed' ? s.error : s.log}</div>}
      {canChange && (s.canRetry || s.canStop) && (
        <div className="automation-step-actions">
          {s.canRetry && s.jobId && (
            <button className="link small" disabled={busy} onClick={() => onAct(`jobs/${s.jobId}/retry`)}>
              <RotateCcw size={12} /> Retry
            </button>
          )}
          {s.canStop && s.jobId && (
            <button className="link small" disabled={busy} onClick={() => onAct(`jobs/${s.jobId}/stop`)}>
              <Square size={12} /> Stop and do by hand
            </button>
          )}
        </div>
      )}
    </li>
  )
}
