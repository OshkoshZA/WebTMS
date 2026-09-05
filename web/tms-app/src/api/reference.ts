import { api } from './client'
import type { Client, CostCentre, Driver, Location, LoadType, Subcontractor, Vehicle } from './types'

// Read-only lookups feeding Loads' dropdowns — no create/edit here, that belongs to
// each resource's own future screen.
export const referenceApi = {
  clients: () => api.get<Client[]>('/clients'),
  loadTypes: () => api.get<LoadType[]>('/load-types'),
  locations: () => api.get<Location[]>('/locations'),
  costCentres: () => api.get<CostCentre[]>('/cost-centres'),
  vehicles: () => api.get<Vehicle[]>('/vehicles'),
  drivers: () => api.get<Driver[]>('/drivers'),
  subcontractors: () => api.get<Subcontractor[]>('/subcontractors'),
}
