import { useEffect, useRef, useState } from 'react'
import { AlertTriangle, Bell, ChevronRight } from 'lucide-react'
import type { AttentionItem, AttentionSummary } from './attention'

/** The bell in the top bar: a count of what needs you, and the list when opened. */
export function AttentionBell({
  summary,
  onOpenItem,
  onViewAll,
}: {
  summary: AttentionSummary | null
  onOpenItem: (item: AttentionItem) => void
  onViewAll: (page: string) => void
}) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const total = summary?.total ?? 0
  const problems = summary?.groups.some((g) => g.severity === 'problem') ?? false

  // Closes on a click elsewhere or Escape.
  useEffect(() => {
    if (!open) return
    const click = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', click)
    document.addEventListener('keydown', key)
    return () => {
      document.removeEventListener('mousedown', click)
      document.removeEventListener('keydown', key)
    }
  }, [open])

  const choose = (action: () => void) => {
    setOpen(false)
    action()
  }
  const label = total === 0 ? 'Nothing needs you' : `${total} thing${total === 1 ? '' : 's'} need${total === 1 ? 's' : ''} you`

  return (
    <div className="bell" ref={box}>
      <button className="icon-button" title={label} aria-label={label} aria-expanded={open} onClick={() => setOpen(!open)}>
        <Bell size={18} />
        {total > 0 && <span className={`bell-count${problems ? ' problem' : ''}`}>{total > 99 ? '99+' : total}</span>}
      </button>
      {open && (
        <div className="bell-panel" role="dialog" aria-label="Needs your attention">
          <div className="bell-heading">Needs your attention</div>
          {!summary || summary.groups.length === 0 ? (
            <p className="muted small bell-empty">Nothing needs you right now.</p>
          ) : (
            summary.groups.map((g) => (
              <section key={g.key} className="bell-group">
                <div className="bell-group-title">
                  {g.severity === 'problem' && <AlertTriangle size={14} className="bell-problem-icon" />}
                  <span>{g.title}</span>
                  <span className={`bell-group-count${g.severity === 'problem' ? ' problem' : ''}`}>{g.count}</span>
                </div>
                <ul>
                  {g.items.map((item, i) => (
                    <li key={`${item.id}-${i}`}>
                      <button className="bell-item" onClick={() => choose(() => onOpenItem(item))}>
                        <span className="bell-item-label">{item.label}</span>
                        {item.detail && <span className="bell-item-detail">{item.detail}</span>}
                      </button>
                    </li>
                  ))}
                </ul>
                {g.count > g.items.length && (
                  <button className="link small bell-more" onClick={() => choose(() => onViewAll(g.page))}>
                    All {g.count} <ChevronRight size={12} />
                  </button>
                )}
              </section>
            ))
          )}
        </div>
      )}
    </div>
  )
}
