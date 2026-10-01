/** What needs the signed-in person now (GET /api/attention). Worked out live; items go once they're dealt with. */
export type AttentionItem = { label: string; detail: string | null; target: 'Request' | 'Case' | 'Page'; id: string }

export type AttentionGroup = {
  key: string
  title: string
  /** "action": waiting for you. "problem": something went wrong. */
  severity: 'action' | 'problem'
  count: number
  /** The page with the full list. */
  page: string
  items: AttentionItem[]
}

export type AttentionSummary = { total: number; groups: AttentionGroup[] }
