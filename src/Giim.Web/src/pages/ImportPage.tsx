import { useState } from 'react'
import { PageHeader } from '../PageHeader'

const fields = [
  { key: 'SerialNumber', label: 'Serial number', required: true },
  { key: 'AssetTag', label: 'Asset tag' },
  { key: 'Manufacturer', label: 'Manufacturer', required: true },
  { key: 'Model', label: 'Model', required: true },
  { key: 'Category', label: 'Type / category', required: true },
  { key: 'AssignedTo', label: 'Assigned to' },
  { key: 'Department', label: 'Department (of the person)' },
  { key: 'Location', label: 'Location' },
  { key: 'PurchaseDate', label: 'Purchase date' },
  { key: 'WarrantyExpiry', label: 'Warranty expiry' },
  { key: 'Status', label: 'Status' },
  { key: 'Supplier', label: 'Supplier' },
  { key: 'Cost', label: 'Cost' },
  { key: 'Notes', label: 'Notes' },
] as const

type Mapping = Partial<Record<string, string>>

type Preview = {
  sheetName: string
  headers: string[]
  mapping: Mapping
  summary: {
    totalRows: number
    toImport: number
    alreadyInRegister: number
    rejected: number
    issueCounts: Record<string, number>
  }
  rows: {
    rowNumber: number
    outcome: 'New' | 'AlreadyInRegister' | 'Rejected'
    issues: string[]
    serialNumber: string | null
    assetTag: string | null
    manufacturer: string | null
    model: string | null
  }[]
}

const issueText: Record<string, string> = {
  BlankSerial: 'Blank serial number',
  DuplicateSerialInFile: 'Serial appears more than once in the file',
  DuplicateAssetTag: 'Asset tag already used',
  MissingManufacturer: 'Missing manufacturer',
  MissingModel: 'Missing model',
  UnknownCategory: 'Type not recognised',
  InvalidDate: 'Date could not be read',
  InvalidCost: 'Cost could not be read',
  UnknownStatus: 'Status not recognised',
  SerialCleaned: 'Serial tidied (spaces / case)',
  ManufacturerRenamed: 'Manufacturer name standardised',
}

async function post(url: string, file: File, mapping?: Mapping): Promise<Preview> {
  const body = new FormData()
  body.append('file', file)
  if (mapping) body.append('mapping', JSON.stringify(mapping))

  const response = await fetch(url, { method: 'POST', body })
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? `Request failed (${response.status})`)
  }
  return response.json()
}

export function ImportPage() {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [mapping, setMapping] = useState<Mapping>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const missingRequired = fields.filter((f) => 'required' in f && !mapping[f.key])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await action()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const choose = (selected: File | null) => {
    setFile(selected)
    setPreview(null)
    setDone(null)
    if (!selected) return
    run(async () => {
      const result = await post('/api/imports/assets/preview', selected)
      setPreview(result)
      setMapping(result.mapping)
    })
  }

  const recheck = () =>
    file && run(async () => setPreview(await post('/api/imports/assets/preview', file, mapping)))

  const commit = () =>
    file &&
    run(async () => {
      const result = await post('/api/imports/assets/commit', file, mapping)
      setDone(`Imported ${result.summary.toImport.toLocaleString()} assets from ${file.name}.`)
      setPreview(null)
      setFile(null)
    })

  return (
    <>
      <PageHeader
        title="Import asset register"
        subtitle="Upload an Excel (.xlsx) register. Nothing is saved until you press Import, and assets already in the register are never overwritten."
      />

      <input type="file" accept=".xlsx" disabled={busy} onChange={(e) => choose(e.target.files?.[0] ?? null)} />
      {busy && <p className="muted">Working…</p>}
      {error && <p className="error">{error}</p>}
      {done && <p className="success">{done}</p>}

      {preview && (
        <>
          <h3>1. Match columns</h3>
          <p className="muted">Sheet “{preview.sheetName}”. Columns were matched automatically; correct any that are wrong.</p>
          <table className="compact">
            <tbody>
              {fields.map((f) => (
                <tr key={f.key}>
                  <td>
                    {f.label}
                    {'required' in f && <span className="required"> *</span>}
                  </td>
                  <td>
                    <select
                      value={mapping[f.key] ?? ''}
                      onChange={(e) => setMapping({ ...mapping, [f.key]: e.target.value || undefined })}
                    >
                      <option value="">Not in this sheet</option>
                      {preview.headers.map((h) => (
                        <option key={h} value={h}>
                          {h}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <button onClick={recheck} disabled={busy || missingRequired.length > 0}>
            Re-check with these columns
          </button>

          <h3>2. Review</h3>
          <div className="stats">
            <Stat label="Rows in sheet" value={preview.summary.totalRows} />
            <Stat label="Will be imported" value={preview.summary.toImport} />
            <Stat label="Already in register" value={preview.summary.alreadyInRegister} />
            <Stat label="Need fixing" value={preview.summary.rejected} />
          </div>

          {Object.keys(preview.summary.issueCounts).length > 0 && (
            <table className="compact">
              <thead>
                <tr>
                  <th>Finding</th>
                  <th>Rows</th>
                </tr>
              </thead>
              <tbody>
                {Object.entries(preview.summary.issueCounts).map(([issue, count]) => (
                  <tr key={issue}>
                    <td>{issueText[issue] ?? issue}</td>
                    <td>{count.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {preview.rows.length > 0 && (
            <>
              <h3>Rows needing attention</h3>
              <p className="muted">Showing up to 500. Rejected rows are skipped; fix them in the sheet and import again.</p>
              <table>
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Result</th>
                    <th>Serial</th>
                    <th>Asset tag</th>
                    <th>Device</th>
                    <th>Findings</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((r) => (
                    <tr key={r.rowNumber}>
                      <td>{r.rowNumber}</td>
                      <td>{r.outcome === 'AlreadyInRegister' ? 'Already in register' : r.outcome === 'New' ? 'Import' : 'Skipped'}</td>
                      <td>{r.serialNumber ?? '-'}</td>
                      <td>{r.assetTag ?? '-'}</td>
                      <td>
                        {r.manufacturer} {r.model}
                      </td>
                      <td>{r.issues.map((i) => issueText[i] ?? i).join('; ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          <h3>3. Import</h3>
          <button className="primary" onClick={commit} disabled={busy || preview.summary.toImport === 0}>
            Import {preview.summary.toImport.toLocaleString()} assets
          </button>
        </>
      )}
    </>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat">
      <div className="stat-value">{value.toLocaleString()}</div>
      <div className="muted">{label}</div>
    </div>
  )
}
