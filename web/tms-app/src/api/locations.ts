import { api } from './client'
import type { Location } from './types'

export interface CreateLocationRequest {
  name: string
  province: string
  countryId: string
}

export type UpdateLocationRequest = CreateLocationRequest

export const locationsApi = {
  list: () => api.get<Location[]>('/locations'),
  get: (id: string) => api.get<Location>(`/locations/${id}`),
  create: (request: CreateLocationRequest) => api.post<Location>('/locations', request),
  update: (id: string, request: UpdateLocationRequest) => api.put<void>(`/locations/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/locations/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/locations/${id}/reactivate`),
}
