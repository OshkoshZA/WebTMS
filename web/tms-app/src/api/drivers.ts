import { api } from './client'
import type { CreateDriverRequest, Driver, UpdateDriverRequest } from './types'

export const driversApi = {
  list: () => api.get<Driver[]>('/drivers'),
  get: (id: string) => api.get<Driver>(`/drivers/${id}`),
  create: (request: CreateDriverRequest) => api.post<Driver>('/drivers', request),
  update: (id: string, request: UpdateDriverRequest) => api.put<void>(`/drivers/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/drivers/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/drivers/${id}/reactivate`),
}
