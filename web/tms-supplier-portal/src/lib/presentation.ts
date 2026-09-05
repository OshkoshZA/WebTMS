export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

const LEG_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Planned
  1: 'info', // Allocated
  2: 'info', // InTransit
  3: 'success', // Delivered
  4: 'success', // PodReceived
}

export function legStatusTone(status: number): Tone {
  return LEG_STATUS_TONE[status] ?? 'neutral'
}

const CONFIRMATION_STATUS_TONE: Record<number, Tone> = {
  0: 'warning', // Issued — awaiting the contact's own response
  1: 'success', // Acknowledged
  2: 'danger', // Declined
}

export function confirmationStatusTone(status: number): Tone {
  return CONFIRMATION_STATUS_TONE[status] ?? 'neutral'
}

const DEBRIEF_STATUS_TONE: Record<number, Tone> = {
  0: 'warning', // PendingReview
  1: 'success', // Approved
}

export function debriefStatusTone(status: number): Tone {
  return DEBRIEF_STATUS_TONE[status] ?? 'neutral'
}

const ACCRUAL_STATUS_TONE: Record<number, Tone> = {
  0: 'info', // Accrued
  1: 'success', // Netted
}

export function accrualStatusTone(status: number): Tone {
  return ACCRUAL_STATUS_TONE[status] ?? 'neutral'
}

const SUPPLIER_INVOICE_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Received
  1: 'success', // Matched
  2: 'danger', // Disputed
}

export function supplierInvoiceStatusTone(status: number): Tone {
  return SUPPLIER_INVOICE_STATUS_TONE[status] ?? 'neutral'
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
