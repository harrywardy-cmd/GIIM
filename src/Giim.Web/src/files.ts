import { api } from './api'

export type FileItem = {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  kind: string
  description: string | null
  uploadedBy: string
  uploadedAt: string
  ticketNumber: string | null
  canPreview: boolean
}

export const fileKinds: [string, string][] = [
  ['Photo', 'Photo'],
  ['Invoice', 'Invoice'],
  ['Warranty', 'Warranty document'],
  ['DisposalCertificate', 'Disposal certificate'],
  ['RepairReport', 'Repair report'],
  ['Other', 'Other'],
]
export const kindLabel = (kind: string) => fileKinds.find(([k]) => k === kind)?.[1] ?? kind

/** What the API accepts (it checks each file's content too). On a phone, this also offers the camera. */
export const acceptedFiles = '.jpg,.jpeg,.png,.webp,.heic,.heif,.pdf,.docx,.xlsx,.msg,.eml,.txt,.csv'
export const maxFileBytes = 20 * 1024 * 1024

export const fileSize = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
export const contentUrl = (assetId: string, fileId: string, download = false) =>
  `/api/assets/${assetId}/attachments/${fileId}/content${download ? '?download=true' : ''}`

/**
 * Uploads files one at a time; a file that fails doesn't stop the others. Without a kind, photos are filed as
 * photos and everything else as "other". Returns a message for each file that failed.
 */
export async function uploadFiles(
  assetId: string,
  files: File[],
  options: { kind?: string; description?: string; ticketNumber?: string },
  onProgress?: (done: number) => void,
): Promise<string[]> {
  const failed: string[] = []
  for (const [i, file] of files.entries()) {
    if (file.size > maxFileBytes) {
      failed.push(`${file.name}: larger than 20 MB`)
    } else {
      const form = new FormData()
      form.append('file', file)
      form.append('kind', options.kind ?? (file.type.startsWith('image/') ? 'Photo' : 'Other'))
      if (options.description) form.append('description', options.description)
      if (options.ticketNumber) form.append('ticketNumber', options.ticketNumber)
      try {
        await api(`/api/assets/${assetId}/attachments`, { method: 'POST', body: form })
      } catch (e) {
        failed.push(`${file.name}: ${(e as Error).message}`)
      }
    }
    onProgress?.(i + 1)
  }
  return failed
}
