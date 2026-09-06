import { api } from './client'
import type { Debrief } from './types'

export const debriefsApi = {
  list: (status?: number) => api.get<Debrief[]>(`/debriefs${status !== undefined ? `?status=${status}` : ''}`),
  get: (id: string) => api.get<Debrief>(`/debriefs/${id}`),
  approve: (id: string, resolutionNote?: string) => api.post<void>(`/debriefs/${id}/approve`, { resolutionNote }),
}
