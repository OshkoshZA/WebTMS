import { api } from './client'
import type { ExceptionRecord } from './types'

export const exceptionsApi = {
  list: (status?: number) => api.get<ExceptionRecord[]>(`/exceptions${status !== undefined ? `?status=${status}` : ''}`),
  get: (id: string) => api.get<ExceptionRecord>(`/exceptions/${id}`),
  acknowledge: (id: string) => api.post<void>(`/exceptions/${id}/acknowledge`),
  resolve: (id: string, resolutionNotes?: string) => api.post<void>(`/exceptions/${id}/resolve`, { resolutionNotes }),
}
