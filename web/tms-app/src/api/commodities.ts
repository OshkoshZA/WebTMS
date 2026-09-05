import { api } from './client'
import type { Commodity } from './types'

export interface CreateCommodityRequest {
  code: string
  name: string
  defaultUnitOfMeasureId: string
  category: number
}

export type UpdateCommodityRequest = CreateCommodityRequest

export const commoditiesApi = {
  list: () => api.get<Commodity[]>('/commodities'),
  get: (id: string) => api.get<Commodity>(`/commodities/${id}`),
  create: (request: CreateCommodityRequest) => api.post<Commodity>('/commodities', request),
  update: (id: string, request: UpdateCommodityRequest) => api.put<void>(`/commodities/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/commodities/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/commodities/${id}/reactivate`),
}
