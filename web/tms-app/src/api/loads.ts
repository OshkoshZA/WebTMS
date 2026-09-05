import { api } from './client'
import type { AddLoadLegRequest, AllocateLoadLegRequest, CreateLoadRequest, Load, LoadLeg } from './types'

export const loadsApi = {
  list: () => api.get<Load[]>('/loads'),
  get: (id: string) => api.get<Load>(`/loads/${id}`),
  create: (request: CreateLoadRequest) => api.post<Load>('/loads', request),
  addLeg: (loadId: string, request: AddLoadLegRequest) => api.post<LoadLeg>(`/loads/${loadId}/legs`, request),
  allocateLeg: (loadId: string, legId: string, request: AllocateLoadLegRequest) =>
    api.post<void>(`/loads/${loadId}/legs/${legId}/allocate`, request),
  startLeg: (loadId: string, legId: string) => api.post<void>(`/loads/${loadId}/legs/${legId}/start`),
  deliverLeg: (loadId: string, legId: string) => api.post<void>(`/loads/${loadId}/legs/${legId}/deliver`),
  hold: (loadId: string, reason: string) => api.post<void>(`/loads/${loadId}/hold`, { reason }),
  releaseHold: (loadId: string) => api.post<void>(`/loads/${loadId}/release-hold`),
  cancel: (loadId: string) => api.post<void>(`/loads/${loadId}/cancel`),
}
