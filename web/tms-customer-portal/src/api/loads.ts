import { api } from './client'
import type { CreateLoadRequest, Load, LoadTracking } from './types'

export const loadsApi = {
  list: () => api.get<Load[]>('/loads'),
  get: (id: string) => api.get<Load>(`/loads/${id}`),
  tracking: (id: string) => api.get<LoadTracking>(`/loads/${id}/tracking`),
  create: (request: CreateLoadRequest) => api.post<Load>('/loads', request),
}
