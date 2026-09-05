// Mirrors the backend's C# enums exactly, by numeric position — the API has no
// JsonStringEnumConverter configured, so every enum crosses the wire as its plain
// underlying int (see Tms.Api/Program.cs). `erasableSyntaxOnly` (tsconfig.app.json)
// rules out TS `enum`, so these are plain readonly label arrays instead: the array
// index IS the wire value, and `label(...)` below looks a value up by that index.

export const LOAD_STATUS = [
  'Quoted', 'Booked', 'Allocated', 'InTransit', 'Delivered',
  'PodReceived', 'Invoiced', 'OnHold', 'Cancelled', 'Closed',
] as const

export const LOAD_LEG_STATUS = ['Planned', 'Allocated', 'InTransit', 'Delivered', 'PodReceived'] as const

export const INVOICE_STATUS = ['Draft', 'Issued', 'PartPaid', 'Paid', 'Void'] as const

export const CREDIT_NOTE_STATUS = ['Draft', 'Issued', 'Void'] as const

export function label(values: readonly string[], value: number): string {
  return values[value] ?? `Unknown (${value})`
}

export interface Load {
  id: string
  referenceNo: string
  loadTypeId: string
  status: number // LOAD_STATUS
  pickupWindowStart: string | null
  pickupWindowEnd: string | null
  deliveryWindowStart: string | null
  deliveryWindowEnd: string | null
}

export interface LoadLeg {
  id: string
  loadId: string
  sequenceNo: number
  status: number // LOAD_LEG_STATUS
}

export interface LoadStatusHistoryEntry {
  loadId: string
  fromStatus: number
  toStatus: number
  changedAt: string
  reason: string | null
}

export interface LoadTracking {
  loadId: string
  status: number
  legs: LoadLeg[]
  history: LoadStatusHistoryEntry[]
}

export interface LoadType {
  id: string
  code: string
  description: string
}

export interface Currency {
  id: string
  code: string
  name: string
  symbol: string
}

export interface CreateLoadRequest {
  clientId: string
  referenceNo: string
  loadTypeId: string
  pickupWindowStart?: string
  pickupWindowEnd?: string
  deliveryWindowStart?: string
  deliveryWindowEnd?: string
}

export interface CreditStatus {
  currencyId: string
  creditLimit: number
  arOutstanding: number
  wip: number
  totalExposure: number
  availableCredit: number
}

export interface InvoiceLine {
  id: string
  rateLineSellId: string
  description: string
  quantity: number
  unitOfMeasureId: string
  rate: number
  amount: number
}

export interface Invoice {
  id: string
  invoiceNumber: string
  clientId: string
  currencyId: string
  financialPeriodId: string
  issueDate: string
  dueDate: string
  status: number // INVOICE_STATUS
  totalExVat: number
  vatAmount: number
  totalIncVat: number
  isOverdue: boolean
  lines: InvoiceLine[]
}

export interface CreditNoteLine {
  id: string
  invoiceLineId: string | null
  description: string
  amount: number
}

export interface CreditNote {
  id: string
  creditNoteNumber: string
  clientId: string
  originalInvoiceId: string | null
  currencyId: string
  financialPeriodId: string
  reason: string
  issueDate: string
  status: number // CREDIT_NOTE_STATUS
  totalAmount: number
  pdfUrl: string | null
  lines: CreditNoteLine[]
}
