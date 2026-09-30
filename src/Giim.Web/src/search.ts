import { useEffect, useState } from 'react'
import { api } from './api'

/** Debounced search; results from older keystrokes are discarded. Pass null to search for nothing. */
export function useSearch<T>(target: string | null) {
  const [results, setResults] = useState<T[]>([])
  useEffect(() => {
    if (!target) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      api<T[] | { people: T[] }>(target, { signal: controller.signal })
        .then((r) => setResults(Array.isArray(r) ? r : r.people))
        .catch(() => undefined)
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [target])
  return target ? results : []
}

export const searchUrl = (term: string, base: string) =>
  term.trim().length >= 2 ? `${base}${encodeURIComponent(term.trim())}` : null
