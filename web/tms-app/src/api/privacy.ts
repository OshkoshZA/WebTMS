import { api } from './client'
import type {
  CreateDataSubjectRequestRequest, DataSubjectRequest, RetentionPolicy, RetentionPolicyRequest,
} from './types'

export const retentionPoliciesApi = {
  list: (companyId: string) => api.get<RetentionPolicy[]>(`/companies/${companyId}/retention-policies`),
  // Replaces the Company's entire policy set in one call — a DataCategory left out of
  // the array has its existing policy removed, never a per-category PUT.
  set: (companyId: string, policies: RetentionPolicyRequest[]) =>
    api.put<RetentionPolicy[]>(`/companies/${companyId}/retention-policies`, policies),
}

export const dataSubjectRequestsApi = {
  list: (status?: number) =>
    api.get<DataSubjectRequest[]>(`/data-subject-requests${status !== undefined ? `?status=${status}` : ''}`),
  get: (id: string) => api.get<DataSubjectRequest>(`/data-subject-requests/${id}`),
  create: (request: CreateDataSubjectRequestRequest) => api.post<DataSubjectRequest>('/data-subject-requests', request),
  fulfill: (id: string) => api.post<void>(`/data-subject-requests/${id}/fulfill`),
  reject: (id: string, rejectionReason: string) => api.post<void>(`/data-subject-requests/${id}/reject`, { rejectionReason }),
  // Shape varies by subject type (Driver vs. User-backed) — the backend returns a
  // plain anonymous object either way, so this stays untyped rather than guessing a
  // shape that would only be half-right.
  export: (id: string) => api.get<Record<string, unknown>>(`/data-subject-requests/${id}/export`),
}
