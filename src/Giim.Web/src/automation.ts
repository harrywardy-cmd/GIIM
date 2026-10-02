/** Starter checklist automation (GET /api/cases/{id}/automation and GET /api/automation). */
export type AgentView = { name: string; lastSeenAt: string; online: boolean; version: string | null; directory: string | null; dryRun: boolean }

export type AutomationStepView = {
  taskId: string
  task: string
  step: string
  description: string
  jobId: string | null
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | null
  runner: 'Agent' | 'Giim'
  claimedBy: string | null
  notBefore: string | null
  attempts: number
  error: string | null
  log: string | null
  dryRun: boolean
  completedAt: string | null
  canRetry: boolean
  canStop: boolean
}

export type CaseAutomation = {
  dryRun: boolean
  agentOnline: boolean
  agents: AgentView[]
  steps: AutomationStepView[]
  readyToStart: number
  cannotStart: string | null
}

export type AutomationOverview = {
  dryRun: boolean
  agents: AgentView[]
  queued: number
  running: number
  waiting: number
  recentFailures: { jobId: string; caseId: string; person: string; step: string; error: string | null; at: string | null }[]
}

const when = (iso: string) => new Date(iso).toLocaleString('en-AU', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** A step's state in a few words. */
export function stepStatusText(s: AutomationStepView): string {
  switch (s.status) {
    case null:
      return 'not started'
    case 'Queued':
      return s.notBefore && new Date(s.notBefore) > new Date() ? `waits until ${when(s.notBefore)}` : 'queued'
    case 'Running':
      return s.runner === 'Giim' ? 'running' : `running on ${s.claimedBy ?? 'agent'}`
    case 'Succeeded':
      return s.dryRun ? 'dry run done' : 'done'
    case 'Failed':
      return 'failed'
    default:
      return 'stopped'
  }
}
