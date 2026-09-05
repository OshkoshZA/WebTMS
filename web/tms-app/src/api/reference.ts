import { api } from './client'
import type {
  Client, ClientCurrency, Commodity, Company, Country, CostCentre, Currency, Driver, ExpenseType, Location, LoadType,
  Subcontractor, SubcontractorCurrency, UnitOfMeasure, Vehicle,
} from './types'

// Read-only lookups feeding other screens' dropdowns — no create/edit here, that
// belongs to each resource's own screen (clientsApi, for Client itself).
export const referenceApi = {
  clients: () => api.get<Client[]>('/clients'),
  companies: () => api.get<Company[]>('/companies'),
  loadTypes: () => api.get<LoadType[]>('/load-types'),
  locations: () => api.get<Location[]>('/locations'),
  costCentres: () => api.get<CostCentre[]>('/cost-centres'),
  vehicles: () => api.get<Vehicle[]>('/vehicles'),
  drivers: () => api.get<Driver[]>('/drivers'),
  subcontractors: () => api.get<Subcontractor[]>('/subcontractors'),
  currencies: () => api.get<Currency[]>('/currencies'),
  commodities: () => api.get<Commodity[]>('/commodities'),
  unitsOfMeasure: () => api.get<UnitOfMeasure[]>('/units-of-measure'),
  countries: () => api.get<Country[]>('/countries'),
  expenseTypes: () => api.get<ExpenseType[]>('/expense-types'),
  clientCurrencies: (clientId: string) => api.get<ClientCurrency[]>(`/clients/${clientId}/currencies`),
  subcontractorCurrencies: (subcontractorId: string) =>
    api.get<SubcontractorCurrency[]>(`/subcontractors/${subcontractorId}/currencies`),
}
