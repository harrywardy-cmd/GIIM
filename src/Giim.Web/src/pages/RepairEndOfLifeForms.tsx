import { useState, type ReactNode } from 'react'
import { api } from '../api'
import { LocationSelect } from '../LocationSelect'
import { uploadFiles } from '../files'
import { FilePicker } from './AssetFiles'

type Done = () => void

function Form({ title, children, error, saving, submitLabel, disabled, onSubmit, onCancel }: {
  title: string
  children: ReactNode
  error: string | null
  saving: boolean
  submitLabel: string
  disabled?: boolean
  onSubmit: () => void
  onCancel: () => void
}) {
  return (
    <div className="action-form">
      <h3>{title}</h3>
      {children}
      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={onSubmit} disabled={saving || disabled}>
        {saving ? 'Saving…' : submitLabel}
      </button>{' '}
      <button onClick={onCancel}>Cancel</button>
    </div>
  )
}

function useSubmit(onDone: Done) {
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  /** `after` runs once the action is saved (e.g. attaching files), before the form closes. */
  const submit = async (url: string, json: unknown, after?: () => Promise<void>) => {
    setSaving(true)
    try {
      await api(url, { method: 'POST', json })
      await after?.()
      onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }
  return { error, saving, submit }
}

export function SendToRepairForm({ assetId, expectedStatus, onDone, onCancel }: { assetId: string; expectedStatus: string; onDone: Done; onCancel: Done }) {
  const [fault, setFault] = useState('')
  const [where, setWhere] = useState<'internal' | 'vendor'>('internal')
  const [vendor, setVendor] = useState('')
  const [warrantyClaim, setWarrantyClaim] = useState(false)
  const [vendorReference, setVendorReference] = useState('')
  const [sentOn, setSentOn] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const { error, saving, submit } = useSubmit(onDone)

  return (
    <Form
      title="Send to repair"
      error={error}
      saving={saving}
      submitLabel="Send to repair"
      disabled={!fault.trim() || (where === 'vendor' && !vendor.trim())}
      onCancel={onCancel}
      onSubmit={() =>
        submit(`/api/assets/${assetId}/repairs`, {
          expectedStatus,
          fault,
          vendor: where === 'vendor' ? vendor : null,
          warrantyClaim: where === 'vendor' && warrantyClaim,
          vendorReference: where === 'vendor' ? vendorReference || null : null,
          sentOn: where === 'vendor' ? sentOn || null : null,
          ticketNumber: ticketNumber || null,
        })
      }
    >
      <div className="form-row">
        <label className="grow">
          Fault (required)
          <input value={fault} onChange={(e) => setFault(e.target.value)} placeholder="e.g. Laptop will not charge" autoFocus />
        </label>
        <label>
          Repaired by
          <select value={where} onChange={(e) => setWhere(e.target.value as 'internal' | 'vendor')}>
            <option value="internal">IT (internal)</option>
            <option value="vendor">Vendor / repairer</option>
          </select>
        </label>
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} placeholder="e.g. INC56111" />
        </label>
      </div>
      {where === 'vendor' && (
        <div className="form-row">
          <label>
            Vendor (required)
            <input value={vendor} onChange={(e) => setVendor(e.target.value)} placeholder="e.g. Dell" />
          </label>
          <label className="inline">
            <input type="checkbox" checked={warrantyClaim} onChange={(e) => setWarrantyClaim(e.target.checked)} /> Warranty claim
          </label>
          <label>
            Vendor case / RMA number
            <input value={vendorReference} onChange={(e) => setVendorReference(e.target.value)} />
          </label>
          <label>
            Date sent
            <input type="date" value={sentOn} onChange={(e) => setSentOn(e.target.value)} />
          </label>
        </div>
      )}
    </Form>
  )
}

export function CompleteRepairForm({ assetId, repairId, onDone, onCancel }: { assetId: string; repairId: string; onDone: Done; onCancel: Done }) {
  const [outcome, setOutcome] = useState<'Repaired' | 'BeyondRepair'>('Repaired')
  const [diagnosis, setDiagnosis] = useState('')
  const [workPerformed, setWorkPerformed] = useState('')
  const [cost, setCost] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const { error, saving, submit } = useSubmit(onDone)

  return (
    <Form
      title="Complete repair"
      error={error}
      saving={saving}
      submitLabel={outcome === 'Repaired' ? 'Mark repaired' : 'Mark beyond repair'}
      disabled={outcome === 'Repaired' && !workPerformed.trim()}
      onCancel={onCancel}
      onSubmit={() =>
        submit(`/api/assets/${assetId}/repairs/${repairId}/complete`, {
          outcome,
          diagnosis: diagnosis || null,
          workPerformed: workPerformed || null,
          cost: cost ? Number(cost) : null,
          ticketNumber: ticketNumber || null,
        })
      }
    >
      <div className="form-row">
        <label>
          Outcome
          <select value={outcome} onChange={(e) => setOutcome(e.target.value as 'Repaired' | 'BeyondRepair')}>
            <option value="Repaired">Repaired</option>
            <option value="BeyondRepair">Beyond repair</option>
          </select>
        </label>
        <label className="grow">
          Diagnosis
          <input value={diagnosis} onChange={(e) => setDiagnosis(e.target.value)} placeholder="e.g. Damaged charging port" />
        </label>
      </div>
      <div className="form-row">
        {outcome === 'Repaired' && (
          <label className="grow">
            Work performed (required)
            <input value={workPerformed} onChange={(e) => setWorkPerformed(e.target.value)} placeholder="e.g. Charging port replaced" />
          </label>
        )}
        <label>
          Cost ($)
          <input type="number" min={0} step="0.01" value={cost} onChange={(e) => setCost(e.target.value)} />
        </label>
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} />
        </label>
      </div>
      <p className="muted small">
        {outcome === 'Repaired'
          ? 'The device goes back to where it came from: its user, or the store (a returned device still needs wiping).'
          : 'The device stays in repair until it is returned from its user (if any) and retired.'}
      </p>
    </Form>
  )
}

const sanitisationOptions = [
  { value: 'Wiped', label: 'Wiped / reimaged by IT', inHand: true },
  { value: 'DriveDestroyed', label: 'Drive removed and destroyed', inHand: true },
  { value: 'NoStorage', label: 'No storage (monitor, dock, peripheral)', inHand: true },
  { value: 'RemoteWipe', label: 'Intune remote wipe', inHand: false },
  { value: 'NotPossible', label: 'Not wiped (no remote wipe possible)', inHand: false },
]

export function RetireForm({ assetId, status, onDone, onCancel }: { assetId: string; status: string; onDone: Done; onCancel: Done }) {
  const missing = status === 'Lost' || status === 'Stolen'
  const options = sanitisationOptions.filter((o) => o.inHand !== missing)
  const [reason, setReason] = useState(missing ? 'Written off' : '')
  const [dataSanitisation, setDataSanitisation] = useState(options[0].value)
  const [finalLocationId, setFinalLocationId] = useState('')
  const [ticketNumber, setTicketNumber] = useState('')
  const { error, saving, submit } = useSubmit(onDone)

  return (
    <Form
      title="Retire"
      error={error}
      saving={saving}
      submitLabel="Retire asset"
      disabled={!reason.trim()}
      onCancel={onCancel}
      onSubmit={() =>
        submit(`/api/assets/${assetId}/actions`, {
          action: 'Retire',
          expectedStatus: status,
          reason,
          dataSanitisation,
          finalLocationId: finalLocationId || null,
          ticketNumber: ticketNumber || null,
        })
      }
    >
      <div className="form-row">
        <label className="grow">
          Reason (required)
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. End of life (5 years)" autoFocus />
        </label>
        <label>
          Data
          <select value={dataSanitisation} onChange={(e) => setDataSanitisation(e.target.value)}>
            {options.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </label>
        {!missing && (
          <label>
            Where it is now
            <LocationSelect value={finalLocationId} onChange={setFinalLocationId} allowNone noneLabel="Leave where it is" />
          </label>
        )}
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} />
        </label>
      </div>
      <p className="muted small">Retired assets can’t be reissued. Remember to retire or delete the device in Intune too.</p>
    </Form>
  )
}

export function DisposeForm({ assetId, onDone, onCancel, onFileProblems }: {
  assetId: string
  onDone: Done
  onCancel: Done
  onFileProblems: (problems: string[]) => void
}) {
  const [method, setMethod] = useState('EWasteRecycling')
  const [company, setCompany] = useState('')
  const [certificateNumber, setCertificateNumber] = useState('')
  const [disposedOn, setDisposedOn] = useState(new Date().toISOString().slice(0, 10))
  const [ticketNumber, setTicketNumber] = useState('')
  const [certificate, setCertificate] = useState<File[]>([])
  const { error, saving, submit } = useSubmit(onDone)
  const auditable = method === 'EWasteRecycling' || method === 'Destroyed'

  return (
    <Form
      title="Record disposal"
      error={error}
      saving={saving}
      submitLabel="Record disposal"
      disabled={auditable && (!company.trim() || !certificateNumber.trim())}
      onCancel={onCancel}
      onSubmit={() =>
        submit(`/api/assets/${assetId}/actions`, {
          action: 'Dispose',
          expectedStatus: 'Retired',
          disposalMethod: method,
          disposalCompany: company || null,
          certificateNumber: certificateNumber || null,
          disposedOn,
          ticketNumber: ticketNumber || null,
        }, async () => {
          if (certificate.length === 0) return
          const problems = await uploadFiles(assetId, certificate, { kind: 'DisposalCertificate', ticketNumber })
          if (problems.length) onFileProblems(problems)
        })
      }
    >
      <div className="form-row">
        <label>
          Method
          <select value={method} onChange={(e) => setMethod(e.target.value)}>
            <option value="EWasteRecycling">E-waste recycling</option>
            <option value="Destroyed">Destroyed</option>
            <option value="ReturnedToVendor">Returned to vendor</option>
            <option value="LeaseReturn">Lease return</option>
            <option value="Sold">Sold</option>
            <option value="Donated">Donated</option>
          </select>
        </label>
        <label>
          Company{auditable && ' (required)'}
          <input value={company} onChange={(e) => setCompany(e.target.value)} />
        </label>
        <label>
          Certificate number{auditable && ' (required)'}
          <input value={certificateNumber} onChange={(e) => setCertificateNumber(e.target.value)} placeholder="e.g. EW-88321" />
        </label>
        <label>
          Date
          <input type="date" value={disposedOn} onChange={(e) => setDisposedOn(e.target.value)} />
        </label>
        <label>
          Ticket
          <input value={ticketNumber} onChange={(e) => setTicketNumber(e.target.value)} />
        </label>
      </div>
      <div className="form-row">
        <FilePicker label="Certificate file (optional, e.g. the PDF from the recycler)" onChange={setCertificate} />
      </div>
      <p className="muted small">This is the final step. The record stays in GIIM permanently for audits.</p>
    </Form>
  )
}
