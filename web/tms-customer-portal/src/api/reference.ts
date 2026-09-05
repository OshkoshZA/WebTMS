import { api } from './client'
import type { Currency, LoadType } from './types'

// The only two reference-data endpoints a Customer Portal contact can reach — every
// master-data controller (Locations, CostCentres, Vehicles, Drivers) is forbidden
// outright to any portal caller, so a booking form can only ever offer a LoadType
// choice, not resolve a leg's origin/destination/resource to a human name. Currencies
// is deliberately left open to any authenticated caller (CurrenciesController itself
// has no portal block) since it's harmless shared reference data.
export const referenceApi = {
  loadTypes: () => api.get<LoadType[]>('/load-types'),
  currencies: () => api.get<Currency[]>('/currencies'),
}
