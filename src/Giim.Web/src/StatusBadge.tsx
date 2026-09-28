import { statusText } from './status'

export function StatusBadge({ status }: { status: string }) {
  return <span className={`status status-${status}`}>{statusText(status)}</span>
}
