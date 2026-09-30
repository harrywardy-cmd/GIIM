/** Device request statuses, priorities and reasons, with the wording the brief uses. */
export const requestStatusLabel: Record<string, string> = {
  PendingApproval: 'Pending approval',
  InfoRequested: 'More info needed',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  Ordered: 'Ordered',
  Received: 'Received',
  Completed: 'Completed',
}

export const reasonLabel: Record<string, string> = {
  NewStarter: 'New starter',
  Replacement: 'Replacement',
  Additional: 'Additional device',
  Upgrade: 'Upgrade',
  Other: 'Other',
}

export const priorities = ['Low', 'Medium', 'High', 'Urgent'] as const

export const requestReference = (number: number) => `REQ${number}`

export type RequestListItem = {
  id: string
  number: number
  deviceDescription: string
  category: string
  recipientId: string
  recipient: string
  department: string | null
  requestedByName: string
  approver: string | null
  status: string
  priority: string
  submittedAt: string
  neededBy: string | null
  ticketNumber: string | null
  purchaseOrder: string | null
}

export type RequestList = { total: number; items: RequestListItem[]; counts: Record<string, number> }
