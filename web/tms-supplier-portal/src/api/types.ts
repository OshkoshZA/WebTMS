// Mirrors the backend's C# enums exactly, by numeric position — the API has no
// JsonStringEnumConverter configured, so every enum crosses the wire as its plain
// underlying int (see Tms.Api/Program.cs). `erasableSyntaxOnly` (tsconfig.app.json)
// rules out TS `enum`, so these are plain readonly label arrays instead: the array
// index IS the wire value, and `label(...)` below looks a value up by that index.

export const LEG_STATUS = ['Planned', 'Allocated', 'InTransit', 'Delivered', 'PodReceived'] as const

export const CONFIRMATION_STATUS = ['Issued', 'Acknowledged', 'Declined'] as const

export const DEBRIEF_STATUS = ['PendingReview', 'Approved'] as const

export const INCIDENT_TYPE = ['Delay', 'Damage', 'Breakdown'] as const

export const INCIDENT_SEVERITY = ['Info', 'Warning', 'Critical'] as const

export const CLAIMED_AGAINST = ['Company', 'SubcontractorAccrual'] as const

export const ACCRUAL_STATUS = ['Accrued', 'Netted'] as const

export const SUPPLIER_INVOICE_STATUS = ['Received', 'Matched', 'Disputed'] as const

export const SUBCONTRACTOR_EXPENSE_STATUS = ['AvailableToExport', 'Exported', 'Paid'] as const

export const EXCEPTION_SEVERITY = ['Info', 'Warning', 'Critical'] as const

export const EXCEPTION_STATUS = ['Open', 'Acknowledged', 'Resolved'] as const

export function label(values: readonly string[], value: number): string {
  return values[value] ?? `Unknown (${value})`
}

export interface Currency {
  id: string
  code: string
  name: string
  symbol: string
}

export interface ExpenseType {
  id: string
  code: string
  name: string
  active: boolean
}

export interface LoadConfirmation {
  id: string
  loadLegId: string
  subcontractorId: string
  documentNumber: string
  issuedDate: string
  status: number // CONFIRMATION_STATUS
  pdfUrl: string | null
  declineReason: string | null
}

export interface SubcontractorLeg {
  id: string
  loadId: string
  sequenceNo: number
  originLocationId: string
  destinationLocationId: string
  status: number // LEG_STATUS
  buyAmount: number
  buyCurrencyId: string | null
  confirmation: LoadConfirmation | null
}

export interface AcknowledgeConfirmationRequest {
  acknowledged: boolean
  reason?: string
}

export interface SubmitDebriefIncidentRequest {
  type: number // INCIDENT_TYPE
  severity: number // INCIDENT_SEVERITY
  narrative: string
}

export interface SubmitDebriefExpenseRequest {
  expenseTypeId: string
  description: string
  amount: number
  currencyId: string
  receiptImageUrl?: string
  claimedAgainst: number // CLAIMED_AGAINST
  accrualId?: string
}

export interface SubmitDebriefRequest {
  odometerStart?: number
  odometerEnd?: number
  fuelLitres?: number
  fuelCost?: number
  drivingHours?: number
  podReceived: boolean
  podImageUrl?: string
  incidents?: SubmitDebriefIncidentRequest[]
  expenses?: SubmitDebriefExpenseRequest[]
}

export interface DebriefIncident {
  id: string
  type: number // INCIDENT_TYPE
  severity: number // INCIDENT_SEVERITY
  narrative: string
}

export interface DebriefExpense {
  id: string
  expenseTypeId: string
  description: string
  amount: number
  currencyId: string
  receiptImageUrl: string | null
  claimedAgainst: number // CLAIMED_AGAINST
  accrualId: string | null
}

export interface Debrief {
  id: string
  loadLegId: string
  driverId: string | null
  vehicleId: string | null
  odometerStart: number | null
  odometerEnd: number | null
  fuelLitres: number | null
  fuelCost: number | null
  drivingHours: number | null
  podReceived: boolean
  podImageUrl: string | null
  submittedAt: string
  status: number // DEBRIEF_STATUS
  exceptionReasons: string | null
  resolvedByUserId: string | null
  resolvedAt: string | null
  resolutionNote: string | null
  incidents: DebriefIncident[]
  expenses: DebriefExpense[]
}

export interface SubcontractorAccrual {
  id: string
  rateLineBuyId: string
  subcontractorId: string
  currencyId: string
  accrualDate: string
  estimatedAmount: number
  status: number // ACCRUAL_STATUS
}

export interface SubcontractorExpense {
  id: string
  rateLineBuyId: string
  accrualId: string
  financialPeriodId: string
  amount: number
  status: number // SUBCONTRACTOR_EXPENSE_STATUS
  finalizedDate: string
}

export interface SupplierInvoice {
  id: string
  subcontractorId: string
  currencyId: string
  supplierInvoiceNumber: string
  invoiceDate: string
  receivedDate: string
  amount: number
  status: number // SUPPLIER_INVOICE_STATUS
  disputeReason: string | null
  expenses: SubcontractorExpense[]
}

export interface ExceptionRecord {
  id: string
  category: string
  severity: number // EXCEPTION_SEVERITY
  entityType: string
  entityId: string
  status: number // EXCEPTION_STATUS
  raisedAt: string
  assignedToUserId: string | null
  description: string
  resolvedAt: string | null
  resolutionNotes: string | null
}
