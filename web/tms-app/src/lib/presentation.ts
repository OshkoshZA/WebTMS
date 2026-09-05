// Which color a status reads as — kept separate from api/types.ts (wire shapes) since
// this is a presentation-only judgment call, not part of the API contract.

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

const LOAD_LEG_STATUS_TONE: Record<number, Tone> = {
  0: 'neutral', // Planned
  1: 'info', // Allocated
  2: 'info', // InTransit
  3: 'success', // Delivered
  4: 'success', // PodReceived
}

export function loadStatusTone(status: number): Tone {
  return LOAD_STATUS_TONE[status] ?? 'neutral'
}

export function loadLegStatusTone(status: number): Tone {
  return LOAD_LEG_STATUS_TONE[status] ?? 'neutral'
}

// Shared by every Active/Deactivated master-data resource (Client, Vehicle,
// Subcontractor, ...) — §11.5's own convention, so one mapping covers them all.
export function activeDeactivatedTone(status: number): Tone {
  return status === 0 ? 'success' : 'neutral'
}

export function formatMoney(amount: number, currencyCode: string): string {
  return `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currencyCode}`
}

export function formatDateTime(value: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}
