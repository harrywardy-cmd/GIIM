import { useEffect, useState } from 'react'
import { FileText, Paperclip } from 'lucide-react'
import { api } from '../api'
import { acceptedFiles, contentUrl, fileKinds, fileSize, kindLabel, uploadFiles, type FileItem } from '../files'
import { useUser } from '../user'

/** A file picker for forms (return, disposal) that attach files once the action is saved. */
export function FilePicker({ label, onChange }: { label: string; onChange: (files: File[]) => void }) {
  return (
    <label className="grow">
      {label}
      <input type="file" multiple accept={acceptedFiles} onChange={(e) => onChange([...(e.target.files ?? [])])} />
    </label>
  )
}

/** Photos and documents kept with an asset. Technicians add and remove; everyone can view. */
export function AssetFiles({ assetId, refreshKey, onChanged }: { assetId: string; refreshKey: number; onChanged: () => void }) {
  const { canChange } = useUser()
  const [files, setFiles] = useState<FileItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    api<FileItem[]>(`/api/assets/${assetId}/attachments`)
      .then((f) => {
        setFiles(f)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [assetId, refreshKey, reload])

  const changed = () => {
    setReload((n) => n + 1)
    onChanged()
  }
  const photos = files?.filter((f) => f.canPreview) ?? []
  const documents = files?.filter((f) => !f.canPreview) ?? []

  return (
    <div className="asset-files">
      <div className="section-title">
        <h3>Files {files && files.length > 0 && `(${files.length})`}</h3>
        {canChange && !adding && (
          <button onClick={() => setAdding(true)}>
            <Paperclip size={16} /> Add files
          </button>
        )}
      </div>
      {error && <p className="error">{error}</p>}
      {adding && (
        <AddFilesForm
          assetId={assetId}
          onDone={() => {
            setAdding(false)
            changed()
          }}
          onCancel={() => setAdding(false)}
        />
      )}
      {files && files.length === 0 && !adding && (
        <p className="muted small">No photos or documents yet{canChange ? ': add invoices, warranty documents or photos of damage.' : '.'}</p>
      )}

      {photos.length > 0 && (
        <div className="file-grid">
          {photos.map((f) => (
            <figure key={f.id} className="file-thumb">
              <a href={contentUrl(assetId, f.id)} target="_blank" rel="noopener" title={f.description ?? f.fileName}>
                <img src={contentUrl(assetId, f.id)} alt={f.description ?? f.fileName} loading="lazy" />
              </a>
              <figcaption className="small muted">
                {kindLabel(f.kind)} · {new Date(f.uploadedAt).toLocaleDateString('en-AU')}
                {canChange && <RemoveFile assetId={assetId} file={f} onRemoved={changed} />}
              </figcaption>
            </figure>
          ))}
        </div>
      )}

      {documents.length > 0 && (
        <ul className="file-list">
          {documents.map((f) => (
            <li key={f.id}>
              <FileText size={18} className="muted" />
              <div className="grow">
                <a href={contentUrl(assetId, f.id, true)}>{f.fileName}</a>
                <div className="small muted">
                  {kindLabel(f.kind)} · {fileSize(f.sizeBytes)} · {f.uploadedBy}, {new Date(f.uploadedAt).toLocaleDateString('en-AU')}
                  {f.ticketNumber && <> · {f.ticketNumber}</>}
                </div>
                {f.description && <div className="small">{f.description}</div>}
              </div>
              {canChange && <RemoveFile assetId={assetId} file={f} onRemoved={changed} />}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function AddFilesForm({ assetId, onDone, onCancel }: { assetId: string; onDone: () => void; onCancel: () => void }) {
  const [chosen, setChosen] = useState<File[]>([])
  const [kind, setKind] = useState('')
  const [description, setDescription] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const [progress, setProgress] = useState<number | null>(null)
  const [failed, setFailed] = useState<string[]>([])

  const upload = async () => {
    setProgress(0)
    const problems = await uploadFiles(assetId, chosen, { kind: kind || undefined, description, ticketNumber }, setProgress)
    setProgress(null)
    if (problems.length === 0) onDone()
    else setFailed(problems)
  }

  return (
    <div className="action-form">
      <div className="form-row">
        <FilePicker label="Files (up to 20 MB each)" onChange={setChosen} />
        <label>
          Type
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">Photos as photos, others as other</option>
            {fileKinds.map(([k, label]) => (
              <option key={k} value={k}>
                {label}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="form-row">
        <label className="grow">
          Description (optional)
          <input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} placeholder="e.g. Cracked screen on return" />
        </label>
        <label>
          Ticket
          <input value={ticketNumber} maxLength={50} onChange={(e) => setTicketNumber(e.target.value)} />
        </label>
      </div>
      {failed.length > 0 && (
        <div className="error small">
          {failed.map((f) => (
            <div key={f}>{f}</div>
          ))}
        </div>
      )}
      <div className="form-row">
        <button className="primary" disabled={chosen.length === 0 || progress !== null} onClick={upload}>
          {progress !== null ? `Uploading ${Math.min(progress + 1, chosen.length)} of ${chosen.length}…` : 'Upload'}
        </button>
        <button onClick={failed.length > 0 ? onDone : onCancel} disabled={progress !== null}>
          {failed.length > 0 ? 'Close' : 'Cancel'}
        </button>
      </div>
    </div>
  )
}

function RemoveFile({ assetId, file, onRemoved }: { assetId: string; file: FileItem; onRemoved: () => void }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  if (!open)
    return (
      <button className="link small" onClick={() => setOpen(true)}>
        Remove
      </button>
    )
  const remove = () =>
    api(`/api/assets/${assetId}/attachments/${file.id}/remove`, { method: 'POST', json: { reason } })
      .then(onRemoved)
      .catch((e: Error) => setError(e.message))
  return (
    <div className="remove-file">
      <input autoFocus value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why remove it?" maxLength={500} />
      <button disabled={!reason.trim()} onClick={remove}>
        Remove
      </button>
      <button onClick={() => setOpen(false)}>Cancel</button>
      {error && <div className="error small">{error}</div>}
    </div>
  )
}
