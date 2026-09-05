import { api } from './client'
import type { CostCentre } from './types'

export interface CreateCostCentreRequest {
  code: string
  name: string
  parentCostCentreId?: string
}

export type UpdateCostCentreRequest = CreateCostCentreRequest

export const costCentresApi = {
  list: () => api.get<CostCentre[]>('/cost-centres'),
  get: (id: string) => api.get<CostCentre>(`/cost-centres/${id}`),
  create: (request: CreateCostCentreRequest) => api.post<CostCentre>('/cost-centres', request),
  update: (id: string, request: UpdateCostCentreRequest) => api.put<void>(`/cost-centres/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/cost-centres/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/cost-centres/${id}/reactivate`),
}
