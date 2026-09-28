/** Display names for asset statuses, matching the brief's wording. */
export const statusLabel: Record<string, string> = {
  Received: 'Received',
  ReadyToDeploy: 'Ready to deploy',
  Assigned: 'Assigned',
  ReturnRequested: 'Return requested',
  Returned: 'Returned',
  Wiped: 'Wiped',
  InRepair: 'In repair',
  Lost: 'Lost',
  Stolen: 'Stolen',
  Retired: 'Retired',
  Disposed: 'Disposed',
}

export const statusText = (status: string | null | undefined) => (status ? (statusLabel[status] ?? status) : '-')
