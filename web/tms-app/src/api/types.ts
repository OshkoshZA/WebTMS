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

export const LOAD_LEG_EXECUTION_TYPE = ['OwnFleet', 'Subcontracted'] as const

export const ACTIVE_DEACTIVATED = ['Active', 'Deactivated'] as const

export const DRIVER_STATUS = ['Active', 'OnLeave', 'Deactivated'] as const

export const VEHICLE_TYPE = ['Horse', 'Trailer', 'Rigid'] as const

export const EXCEPTION_SEVERITY = ['Info', 'Warning', 'Critical'] as const

export const EXCEPTION_STATUS = ['Open', 'Acknowledged', 'Resolved'] as const

export const COMMODITY_CATEGORY = ['Fuel', 'BulkLiquid', 'DryBulk', 'BreakBulk', 'General'] as const

export const INVOICE_STATUS = ['Draft', 'Issued', 'PartPaid', 'Paid', 'Void'] as const

export const CREDIT_NOTE_STATUS = ['Draft', 'Issued', 'Void'] as const

export const CONFIRMATION_STATUS = ['Issued', 'Acknowledged', 'Declined'] as const

export const FINANCIAL_YEAR_STATUS = ['Future', 'Open', 'Closed'] as const

export const FINANCIAL_PERIOD_STATUS = ['Future', 'Open', 'Closed'] as const

// Two-state, but one-directional unlike ACTIVE_DEACTIVATED — there is no "reactivate"
// for an ApiClient, only Create (a new one) — RevokeApiClient has no reverse action.
export const API_CLIENT_STATUS = ['Active', 'Revoked'] as const

// One-directional, same shape as API_CLIENT_STATUS — no re-enable action exists.
export const WEBHOOK_SUBSCRIPTION_STATUS = ['Active', 'Disabled'] as const

export const WEBHOOK_DELIVERY_STATUS = ['Pending', 'Delivered', 'Failed'] as const

// Mirrors WebhookEventTypes.All server-side (src/Tms.Modules.Integration) — there is
// no GET catalog endpoint for this the way Functions has GET /functions, so the list
// is hardcoded here and must be kept in sync with that file by hand.
export const WEBHOOK_EVENT_TYPES = [
  'load.status_changed',
  'loadconfirmation.issued',
  'debrief.approved',
  'invoice.issued',
  'creditnote.issued',
  'subcontractor_accrual.raised',
  'subcontractor_invoice.received',
  'subcontractor_expense.available_for_export',
  'financialperiod.closed',
  'exception.raised',
] as const

// Of the 10 documented event types, only these actually fire today — every other
// controller action that could raise one hasn't been wired to WebhookPublisher yet.
// A UI-only hint (§11.3), not a backend concept, so a registration for a dormant type
// isn't mistaken for broken.
export const WEBHOOK_EVENT_TYPES_LIVE = new Set<string>([
  'invoice.issued',
  'creditnote.issued',
  'subcontractor_expense.available_for_export',
])

export const DATA_CATEGORY = ['FinancialRecords', 'DriverPersonalData', 'PortalContactData', 'AuditTrail'] as const

export const DSR_SUBJECT_TYPE = ['Driver', 'User', 'ClientContact', 'SubcontractorContact'] as const

export const DSR_REQUEST_TYPE = ['Access', 'Rectification', 'Erasure', 'Portability'] as const

// InProgress exists in the wire enum but DataSubjectRequestsController never actually
// sets it anywhere — every request goes straight from Received to Fulfilled/Rejected —
// so no UI action here ever targets it, only Received/Fulfilled/Rejected are reachable.
export const DSR_STATUS = ['Received', 'InProgress', 'Fulfilled', 'Rejected'] as const

export const AUDIT_ACTION = ['Create', 'Update', 'Delete', 'StatusChange', 'Approve', 'Override'] as const

export const DEBRIEF_STATUS = ['PendingReview', 'Approved'] as const

export const INCIDENT_TYPE = ['Delay', 'Damage', 'Breakdown'] as const

export const INCIDENT_SEVERITY = ['Info', 'Warning', 'Critical'] as const

export const CLAIMED_AGAINST = ['Company', 'SubcontractorAccrual'] as const

export const SUBCONTRACTOR_ACCRUAL_STATUS = ['Accrued', 'Netted'] as const

export const SUPPLIER_INVOICE_STATUS = ['Received', 'Matched', 'Disputed'] as const

export const SUBCONTRACTOR_EXPENSE_STATUS = ['AvailableToExport', 'Exported', 'Paid'] as const

export function label(values: readonly string[], value: number): string {
  return values[value] ?? `Unknown (${value})`
}

export interface Load {
  id: string
  clientId: string
  referenceNo: string
  loadTypeId: string
  status: number // LOAD_STATUS
  pickupWindowStart: string | null
  pickupWindowEnd: string | null
  deliveryWindowStart: string | null
  deliveryWindowEnd: string | null
  legs: LoadLeg[]
}

export interface LoadLeg {
  id: string
  loadId: string
  sequenceNo: number
  originLocationId: string
  destinationLocationId: string
  executionType: number // LOAD_LEG_EXECUTION_TYPE
  status: number // LOAD_LEG_STATUS
  costCentreId: string
  vehicleId: string | null
  driverId: string | null
  subcontractorId: string | null
}

export interface CreateLoadRequest {
  clientId: string
  referenceNo: string
  loadTypeId: string
  creditOverrideReason?: string
  pickupWindowStart?: string
  pickupWindowEnd?: string
  deliveryWindowStart?: string
  deliveryWindowEnd?: string
}

export interface AddLoadLegRequest {
  sequenceNo: number
  originLocationId: string
  destinationLocationId: string
  executionType: number
  costCentreId: string
  vehicleId?: string
  driverId?: string
  subcontractorId?: string
}

export interface AllocateLoadLegRequest {
  vehicleId?: string
  driverId?: string
  subcontractorId?: string
}

export interface Commodity {
  id: string
  code: string
  name: string
  defaultUnitOfMeasureId: string
  category: number // COMMODITY_CATEGORY
  active: boolean
}

export interface UnitOfMeasure {
  id: string
  code: string
  description: string
}

export interface AddCommodityLineRequest {
  commodityId: string
  quantity: number
  unitOfMeasureId: string
  sellRatePerUnit: number
  buyRatePerUnit?: number
  sellCurrencyId?: string
  buyCurrencyId?: string
  creditOverrideReason?: string
}

export interface CommodityLine {
  id: string
  loadLegId: string
  commodityId: string
  quantity: number
  unitOfMeasureId: string
  sequenceNo: number
  sellCurrencyId: string
  sellRatePerUnit: number
  sellAmount: number
  buyCurrencyId: string | null
  buyRatePerUnit: number | null
  buyAmount: number | null
}

export interface LoadLegMargin {
  legId: string
  sellCurrencyId: string | null
  sellTotal: number
  buyCurrencyId: string | null
  buyTotal: number
  exchangeRateUsed: number | null
  convertedBuyTotal: number | null
  margin: number | null
  note: string | null
}

export interface LoadMargin {
  loadId: string
  legs: LoadLegMargin[]
}

export interface Client {
  id: string
  name: string
  registrationNo: string
  currencyId: string
  creditLimit: number
  paymentTermsDays: number
  defaultCostCentreId: string | null
  status: number // ACTIVE_DEACTIVATED
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

export interface ClientCurrency {
  id: string
  clientId: string
  currencyId: string
  creditLimit: number
}

export interface CreditStatus {
  currencyId: string
  creditLimit: number
  arOutstanding: number
  wip: number
  totalExposure: number
  availableCredit: number
}

export interface CreateClientRequest {
  name: string
  registrationNo: string
  currencyId: string
  creditLimit: number
  paymentTermsDays: number
}

export interface UpdateClientRequest {
  name: string
  registrationNo: string
  creditLimit: number
  paymentTermsDays: number
}

export interface Location {
  id: string
  name: string
  province: string
  countryId: string
  active: boolean
}

export interface Country {
  id: string
  code: string
  name: string
}

export interface ExpenseType {
  id: string
  code: string
  name: string
  active: boolean
}

export interface CostCentre {
  id: string
  code: string
  name: string
  parentCostCentreId: string | null
  active: boolean
}

export interface Vehicle {
  id: string
  fleetNo: string
  registration: string
  type: number // VEHICLE_TYPE
  make: string | null
  model: string | null
  licenceExpiry: string | null
  vehicleTestExpiry: string | null
  status: number // ACTIVE_DEACTIVATED
}

export interface CreateVehicleRequest {
  fleetNo: string
  registration: string
  type: number
  make?: string
  model?: string
  licenceExpiry?: string
  vehicleTestExpiry?: string
}

export type UpdateVehicleRequest = CreateVehicleRequest

export interface Driver {
  id: string
  employeeNo: string
  name: string
  licenceCode: string
  licenceExpiry: string | null
  pdpExpiry: string | null
  homeCostCentreId: string | null
  status: number // DRIVER_STATUS
}

export interface CreateDriverRequest {
  employeeNo: string
  name: string
  licenceCode: string
  licenceExpiry?: string
  pdpExpiry?: string
  homeCostCentreId?: string
}

export interface UpdateDriverRequest {
  name: string
  licenceCode: string
  licenceExpiry?: string
  pdpExpiry?: string
  homeCostCentreId?: string
  status: number
}

export interface Subcontractor {
  id: string
  name: string
  registrationNo: string
  currencyId: string
  insuranceExpiry: string | null
  bankingDetails: string | null
  paymentTermsDays: number
  status: number // ACTIVE_DEACTIVATED
}

export interface SubcontractorCurrency {
  id: string
  subcontractorId: string
  currencyId: string
}

export interface CreateSubcontractorRequest {
  name: string
  registrationNo: string
  currencyId: string
  insuranceExpiry?: string
  bankingDetails?: string
  paymentTermsDays: number
}

export interface UpdateSubcontractorRequest {
  name: string
  registrationNo: string
  insuranceExpiry?: string
  bankingDetails?: string
  paymentTermsDays: number
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
  pdfUrl: string | null
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

export interface GenerateInvoiceRequest {
  clientId: string
  currencyId?: string
  issueDate?: string
}

export interface CreateCreditNoteLineRequest {
  invoiceLineId?: string
  description: string
  amount: number
}

export interface CreateCreditNoteRequest {
  clientId: string
  originalInvoiceId?: string
  reason: string
  currencyId?: string
  lines: CreateCreditNoteLineRequest[]
  issueDate?: string
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
  status: number // LOAD_LEG_STATUS
  buyAmount: number
  buyCurrencyId: string | null
  confirmation: LoadConfirmation | null
}

export interface FinancialPeriod {
  id: string
  periodNumber: number
  name: string
  startDate: string
  endDate: string
  status: number // FINANCIAL_PERIOD_STATUS
  closedAt: string | null
}

export interface FinancialYear {
  id: string
  yearLabel: string
  startDate: string
  endDate: string
  status: number // FINANCIAL_YEAR_STATUS
  periods: FinancialPeriod[]
}

export interface CreateFinancialYearRequest {
  yearLabel: string
  startDate: string
  endDate: string
  periodCount?: number
}

export interface DebtorsAgingSnapshot {
  id: string
  clientId: string
  currentAmount: number
  days30: number
  days60: number
  days90: number
  days90Plus: number
  totalOutstanding: number
  snapshotDate: string
}

export interface ExchangeRate {
  id: string
  fromCurrencyId: string
  toCurrencyId: string
  effectiveDate: string
  rate: number
}

export interface CaptureExchangeRateRequest {
  fromCurrencyId: string
  toCurrencyId: string
  effectiveDate: string
  rate: number
}

export interface Company {
  id: string
  legalName: string
  tradingName: string | null
  registrationNo: string
  vatNumber: string
  physicalAddress: string
  postalAddress: string
  bankingDetails: string
  invoiceNumberPrefix: string
  logoUrl: string | null
  invoicingEnabled: boolean
  countryId: string
  currencyId: string
}

export interface UpdateCompanyRequest {
  legalName: string
  tradingName?: string
  registrationNo: string
  vatNumber: string
  physicalAddress: string
  postalAddress: string
  bankingDetails: string
  invoiceNumberPrefix: string
  logoUrl?: string
  invoicingEnabled: boolean
}

export interface AppFunction {
  id: string
  code: string
  description: string
}

export interface Role {
  id: string
  name: string
  functions: AppFunction[]
}

export interface CreateRoleRequest {
  name: string
}

export interface UserCompanyRole {
  id: string
  companyId: string
  roleId: string
  roleName: string
}

export interface User {
  id: string
  email: string
  displayName: string
  status: number // ACTIVE_DEACTIVATED
  companyRoles: UserCompanyRole[]
}

export interface CreateUserRequest {
  email: string
  password: string
  displayName: string
  initialCompanyId?: string
  initialRoleId?: string
}

export interface UpdateUserRequest {
  displayName: string
}

export interface AddCompanyRoleRequest {
  companyId: string
  roleId: string
}

// Shared by ClientContactResponse/SubcontractorContactResponse — identical shapes,
// each backed by the same ApplicationUser table as an internal User (§13.1).
export interface PortalContact {
  id: string
  email: string
  displayName: string
  status: number // ACTIVE_DEACTIVATED
}

export interface CreatePortalContactRequest {
  email: string
  password: string
  displayName: string
  roleId: string
}

export interface ApiClient {
  id: string
  name: string
  clientId: string
  status: number // API_CLIENT_STATUS
  rateLimitPerMinute: number
  createdAt: string
}

export interface CreateApiClientRequest {
  name: string
  roleId: string
  rateLimitPerMinute?: number
}

// The one and only time a plaintext secret is ever returned — Create/RotateSecret's
// own response shapes, never part of the plain ApiClient the list/detail views use.
export interface CreateApiClientResponse {
  id: string
  name: string
  clientId: string
  clientSecret: string
  rateLimitPerMinute: number
}

export interface RotateSecretResponse {
  clientSecret: string
}

export interface WebhookSubscription {
  id: string
  eventType: string
  callbackUrl: string
  status: number // WEBHOOK_SUBSCRIPTION_STATUS
}

export interface CreateWebhookSubscriptionRequest {
  eventType: string
  callbackUrl: string
}

// The one and only time a plaintext signing secret is ever returned — Create's own
// response shape, never part of the plain WebhookSubscription the list/detail views use.
export interface CreateWebhookSubscriptionResponse {
  id: string
  eventType: string
  callbackUrl: string
  status: number
  secret: string
}

export interface WebhookDelivery {
  id: string
  subscriptionId: string
  eventType: string
  entityType: string
  entityId: string
  occurredAtUtc: string
  status: number // WEBHOOK_DELIVERY_STATUS
  attemptedAtUtc: string | null
  responseStatusCode: number | null
  errorDetail: string | null
  attemptCount: number
  nextAttemptAtUtc: string | null
}

export interface RetentionPolicy {
  id: string
  dataCategory: number // DATA_CATEGORY
  retentionPeriodYears: number
  legalBasis: string
  anonymizeAfterExpiry: boolean
}

export interface RetentionPolicyRequest {
  dataCategory: number
  retentionPeriodYears: number
  legalBasis: string
  anonymizeAfterExpiry: boolean
}

export interface DataSubjectRequest {
  id: string
  subjectType: number // DSR_SUBJECT_TYPE
  subjectId: string
  requestType: number // DSR_REQUEST_TYPE
  status: number // DSR_STATUS
  receivedAt: string
  dueDate: string
  fulfilledAt: string | null
  rejectionReason: string | null
  handledByUserId: string
}

export interface CreateDataSubjectRequestRequest {
  subjectType: number
  subjectId: string
  requestType: number
}

export interface AuditEntry {
  id: string
  companyId: string | null
  entityType: string
  entityId: string
  action: number // AUDIT_ACTION
  changedByUserId: string | null
  changedByApiClientId: string | null
  changedAtUtc: string
  oldValueJson: string | null
  newValueJson: string | null
  reason: string | null
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

export interface AcknowledgeConfirmationRequest {
  acknowledged: boolean
  reason?: string
}

export interface SubmitDebriefIncidentRequest {
  type: number
  severity: number
  narrative: string
}

export interface SubmitDebriefExpenseRequest {
  expenseTypeId: string
  description: string
  amount: number
  currencyId: string
  receiptImageUrl?: string
  claimedAgainst: number
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

export interface UnconvertedMarginAmount {
  currencyId: string
  side: string // 'Sell' | 'Buy'
  amount: number
}

export interface MarginSummary {
  reportingCurrencyId: string
  sellTotal: number
  buyTotal: number
  margin: number
  unconverted: UnconvertedMarginAmount[]
}

export interface CurrencyExposureTotal {
  currencyId: string
  totalCreditLimit: number
  totalArOutstanding: number
  totalWip: number
  totalExposure: number
}

export interface CreditExposureSummary {
  byCurrency: CurrencyExposureTotal[]
}

export interface PayablesSummary {
  accrued: number
  availableToExport: number
  exported: number
  paid: number
}

export interface SubcontractorAccrual {
  id: string
  rateLineBuyId: string
  subcontractorId: string
  currencyId: string
  accrualDate: string
  estimatedAmount: number
  status: number // SUBCONTRACTOR_ACCRUAL_STATUS
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

export interface CreateSupplierInvoiceRequest {
  subcontractorId: string
  supplierInvoiceNumber: string
  invoiceDate: string
  receivedDate: string
  amount: number
  currencyId?: string
}

export interface MatchSupplierInvoiceResponse {
  invoice: SupplierInvoice
  varianceAmount: number
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
