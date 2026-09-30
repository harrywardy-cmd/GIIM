import { requestStatusLabel } from './requests'

export function RequestStatusBadge({ status }: { status: string }) {
  return <span className={`status request-${status}`}>{requestStatusLabel[status] ?? status}</span>
}

export function PriorityBadge({ priority }: { priority: string }) {
  return <span className={`priority priority-${priority}`}>{priority}</span>
}
