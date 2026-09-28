import { useEffect, useState } from 'react'
import { api } from './api'

export type LocationOption = { id: string; name: string; kind?: string; holdsStock?: boolean; isActive: boolean }

/**
 * Drop-down of managed locations. With stockOnly, lists places that hold stock (and any that still have stock
 * on the books). Locations are managed on the Locations page, so every form picks from the same list.
 */
export function LocationSelect({
  value,
  onChange,
  stockOnly = false,
  allowNone = false,
  noneLabel = 'Not specified',
  onLoaded,
}: {
  value: string
  onChange: (id: string) => void
  stockOnly?: boolean
  allowNone?: boolean
  noneLabel?: string
  onLoaded?: (locations: LocationOption[]) => void
}) {
  const [locations, setLocations] = useState<LocationOption[]>([])

  useEffect(() => {
    api<LocationOption[]>(stockOnly ? '/api/stock/locations' : '/api/locations?activeOnly=true')
      .then((l) => {
        setLocations(l)
        onLoaded?.(l)
      })
      .catch(() => setLocations([]))
    // onLoaded is a one-off hook for the caller's default; reloading when it changes identity isn't wanted.
  }, [stockOnly]) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <select value={value} onChange={(e) => onChange(e.target.value)}>
      {(allowNone || !value) && <option value="">{allowNone ? noneLabel : 'Choose…'}</option>}
      {locations.map((l) => (
        <option key={l.id} value={l.id} disabled={!l.isActive}>
          {l.name}
          {!l.isActive && ' (closed)'}
        </option>
      ))}
    </select>
  )
}
