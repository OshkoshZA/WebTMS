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
