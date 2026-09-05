export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

const LOAD_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Quoted
  1: 'info', // Booked
  2: 'info', // Allocated
  3: 'info', // InTransit
  4: 'success', // Delivered
  5: 'success', // PodReceived
  6: 'success', // Invoiced
  7: 'warning', // OnHold
  8: 'danger', // Cancelled
  9: 'neutral', // Closed
}

export function loadStatusTone(status: number): Tone {
  return LOAD_STATUS_TONE[status] ?? 'neutral'
}

const INVOICE_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Draft
  1: 'info', // Issued
  2: 'warning', // PartPaid
  3: 'success', // Paid
  4: 'neutral', // Void
}

export function invoiceStatusTone(status: number): Tone {
  return INVOICE_STATUS_TONE[status] ?? 'neutral'
}

const CREDIT_NOTE_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Draft
  1: 'info', // Issued
  2: 'neutral', // Void
}

export function creditNoteStatusTone(status: number): Tone {
  return CREDIT_NOTE_STATUS_TONE[status] ?? 'neutral'
}

export function formatDate(value: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

export function formatDateTime(value: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

export function formatMoney(amount: number, currencyCode: string): string {
  return `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currencyCode}`
}
