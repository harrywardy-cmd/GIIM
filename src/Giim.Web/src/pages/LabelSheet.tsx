import { useEffect, useState } from 'react'
import { Printer } from 'lucide-react'
import { api } from '../api'
import { PageHeader } from '../PageHeader'

type Label = { id: string; assetTag: string | null; serialNumber: string; manufacturer: string; model: string; category: string; qr: string }

/**
 * Standard A4 sticker sheets. Measurements in millimetres from the manufacturers' templates:
 * page margins, label size and the pitch (label + gap) between columns and rows.
 */
const layouts = {
  L7160: { name: '21 per sheet · 63.5 × 38.1 mm (e.g. Avery L7160)', cols: 3, rows: 7, width: 63.5, height: 38.1, top: 15.15, left: 7.25, pitchX: 66.04, pitchY: 38.1, small: false },
  L7651: { name: '65 per sheet · 38.1 × 21.2 mm (e.g. Avery L7651)', cols: 5, rows: 13, width: 38.1, height: 21.2, top: 10.7, left: 4.67, pitchX: 40.64, pitchY: 21.2, small: true },
} as const

type LayoutKey = keyof typeof layouts

/** Printable label sheet: QR code, asset tag, serial and model on each label. */
export function LabelSheet({ assetIds, onBack }: { assetIds: string[]; onBack: () => void }) {
  const [labels, setLabels] = useState<Label[]>([])
  const [error, setError] = useState<string | null>(null)
  const [layoutKey, setLayoutKey] = useState<LayoutKey>('L7160')
  const [startAt, setStartAt] = useState(1)

  useEffect(() => {
    api<Label[]>('/api/assets/labels', { method: 'POST', json: { assetIds } })
      .then(setLabels)
      .catch((e: Error) => setError(e.message))
  }, [assetIds])

  const layout = layouts[layoutKey]
  const perSheet = layout.cols * layout.rows
  // Skip labels already used on a partly used sheet.
  const slots: (Label | null)[] = [...Array<null>(Math.min(Math.max(startAt, 1), perSheet) - 1).fill(null), ...labels]
  const sheets = Array.from({ length: Math.max(1, Math.ceil(slots.length / perSheet)) }, (_, i) => slots.slice(i * perSheet, (i + 1) * perSheet))

  return (
    <>
      <div className="no-print">
        <PageHeader
          title="Print labels"
          subtitle={`${labels.length} label${labels.length === 1 ? '' : 's'} on ${sheets.length} A4 sheet${sheets.length === 1 ? '' : 's'}.`}
          actions={
            <>
              <button onClick={onBack}>Back</button>
              <button className="primary" disabled={labels.length === 0} onClick={() => window.print()}>
                <Printer size={16} /> Print
              </button>
            </>
          }
        />
        {error && <p className="error">{error}</p>}
        <div className="form-row">
          <label>
            Label sheet
            <select value={layoutKey} onChange={(e) => setLayoutKey(e.target.value as LayoutKey)}>
              {Object.entries(layouts).map(([key, l]) => (
                <option key={key} value={key}>
                  {l.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Start at label
            <input type="number" min={1} max={perSheet} value={startAt} onChange={(e) => setStartAt(Number(e.target.value))} />
          </label>
        </div>
        <p className="muted small">
          Print at <strong>100% / actual size</strong> with margins set to <strong>none</strong>. Test on plain paper first and hold it
          against a label sheet to check alignment. “Start at label” skips labels already used on a partly used sheet.
        </p>
      </div>

      <div className="label-sheets">
        {sheets.map((sheet, s) => (
          <div key={s} className="label-page">
            {sheet.map((label, i) => {
              const col = i % layout.cols
              const row = Math.floor(i / layout.cols)
              return (
                <div
                  key={i}
                  className={`label ${layout.small ? 'label-small' : ''}`}
                  style={{
                    left: `${layout.left + col * layout.pitchX}mm`,
                    top: `${layout.top + row * layout.pitchY}mm`,
                    width: `${layout.width}mm`,
                    height: `${layout.height}mm`,
                  }}
                >
                  {label && (
                    <>
                      {/* SVG from our own API (QRCoder), not user content. */}
                      <div className="label-qr" dangerouslySetInnerHTML={{ __html: label.qr }} />
                      <div className="label-text">
                        <strong>{label.assetTag ?? label.serialNumber}</strong>
                        {!layout.small && (
                          <>
                            <span>S/N {label.serialNumber}</span>
                            <span>
                              {label.manufacturer} {label.model}
                            </span>
                          </>
                        )}
                        <span className="label-brand">IT asset · scan to open</span>
                      </div>
                    </>
                  )}
                </div>
              )
            })}
          </div>
        ))}
      </div>
    </>
  )
}
