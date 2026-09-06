import { api } from './client'
import type { ApiClient, CreateApiClientRequest, CreateApiClientResponse, RotateSecretResponse } from './types'

export const apiClientsApi = {
  list: () => api.get<ApiClient[]>('/api-clients'),
  get: (id: string) => api.get<ApiClient>(`/api-clients/${id}`),
  create: (request: CreateApiClientRequest) => api.post<CreateApiClientResponse>('/api-clients', request),
  updateRateLimit: (id: string, rateLimitPerMinute: number) =>
    api.put<void>(`/api-clients/${id}/rate-limit`, { rateLimitPerMinute }),
  rotateSecret: (id: string) => api.post<RotateSecretResponse>(`/api-clients/${id}/secrets`),
  revoke: (id: string) => api.post<void>(`/api-clients/${id}/revoke`),
}
