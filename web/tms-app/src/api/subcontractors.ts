import { api } from './client'
import type {
  CreatePortalContactRequest, CreateSubcontractorRequest, PortalContact, Subcontractor, SubcontractorCurrency,
  SubcontractorLeg, UpdateSubcontractorRequest,
} from './types'

export const subcontractorsApi = {
  list: () => api.get<Subcontractor[]>('/subcontractors'),
  get: (id: string) => api.get<Subcontractor>(`/subcontractors/${id}`),
  create: (request: CreateSubcontractorRequest) => api.post<Subcontractor>('/subcontractors', request),
  update: (id: string, request: UpdateSubcontractorRequest) => api.put<void>(`/subcontractors/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/subcontractors/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/subcontractors/${id}/reactivate`),
  currencies: (id: string) => api.get<SubcontractorCurrency[]>(`/subcontractors/${id}/currencies`),
  addCurrency: (id: string, currencyId: string) =>
    api.post<SubcontractorCurrency>(`/subcontractors/${id}/currencies`, { currencyId }),
  legs: (id: string) => api.get<SubcontractorLeg[]>(`/subcontractors/${id}/legs`),
  contacts: (id: string) => api.get<PortalContact[]>(`/subcontractors/${id}/contacts`),
  createContact: (id: string, request: CreatePortalContactRequest) =>
    api.post<PortalContact>(`/subcontractors/${id}/contacts`, request),
  deactivateContact: (id: string, contactId: string) =>
    api.post<void>(`/subcontractors/${id}/contacts/${contactId}/deactivate`),
  reactivateContact: (id: string, contactId: string) =>
    api.post<void>(`/subcontractors/${id}/contacts/${contactId}/reactivate`),
}
