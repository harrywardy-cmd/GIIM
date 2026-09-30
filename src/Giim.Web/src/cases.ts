/** Starter and leaver checklist wording, shared by the list, details and profile pages. */
export const caseStatusLabel: Record<string, string> = {
  Open: 'Not started',
  InProgress: 'In progress',
  WaitingOnSync: 'Waiting on sync',
  Blocked: 'Blocked',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
}

export const caseTypeLabel: Record<string, string> = { Onboarding: 'Starter', Offboarding: 'Leaver' }

export const profileItemTypeLabel: Record<string, string> = {
  Hardware: 'Device',
  StockItem: 'Stock item',
  Application: 'App',
  SecurityGroup: 'Security group',
  LicenceGroup: 'Licence group',
  ManualTask: 'Other task',
}

export type CaseListItem = {
  id: string
  type: string
  status: string
  personId: string
  person: string
  department: string | null
  dueDate: string | null
  ticketNumber: string | null
  tasks: number
  finished: number
  createdAt: string
}

export type CaseList = { items: CaseListItem[]; counts: Record<string, number> }

/** Whole days from today (local time) to a yyyy-mm-dd date; negative if it has passed. */
export function daysUntil(date: string) {
  const today = new Date(new Date().toLocaleDateString('en-CA'))
  return Math.round((new Date(date).getTime() - today.getTime()) / 86_400_000)
}

/** "in 3 days", "today", "2 days ago", from a yyyy-mm-dd date. */
export function relativeDay(date: string | null) {
  if (!date) return ''
  const days = daysUntil(date)
  if (days === 0) return 'today'
  if (days === 1) return 'tomorrow'
  if (days === -1) return 'yesterday'
  return days > 0 ? `in ${days} days` : `${-days} days ago`
}
