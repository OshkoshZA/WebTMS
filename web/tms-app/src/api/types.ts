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
  type: number
  status: number // ACTIVE_DEACTIVATED
}

export interface Driver {
  id: string
  employeeNo: string
  name: string
  status: number // DRIVER_STATUS
}

export interface Subcontractor {
  id: string
  name: string
  registrationNo: string
  status: number // ACTIVE_DEACTIVATED
}
