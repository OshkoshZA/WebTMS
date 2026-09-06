import { api } from './client'
import type {
  Client, ClientAgingBuckets, ClientCurrency, CreateClientRequest, CreatePortalContactRequest, CreditNote, CreditStatus, Invoice,
  PortalContact, UpdateClientRequest,
} from './types'

export const clientsApi = {
  list: () => api.get<Client[]>('/clients'),
  get: (id: string) => api.get<Client>(`/clients/${id}`),
  create: (request: CreateClientRequest) => api.post<Client>('/clients', request),
  update: (id: string, request: UpdateClientRequest) => api.put<void>(`/clients/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/clients/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/clients/${id}/reactivate`),
  creditStatus: (id: string, currencyId?: string) =>
    api.get<CreditStatus>(`/clients/${id}/credit-status${currencyId ? `?currencyId=${currencyId}` : ''}`),
  liveAging: (id: string) => api.get<ClientAgingBuckets>(`/clients/${id}/aging/live`),
  currencies: (id: string) => api.get<ClientCurrency[]>(`/clients/${id}/currencies`),
  addCurrency: (id: string, currencyId: string, creditLimit: number) =>
    api.post<ClientCurrency>(`/clients/${id}/currencies`, { currencyId, creditLimit }),
  updateCurrency: (id: string, currencyId: string, creditLimit: number) =>
    api.put<void>(`/clients/${id}/currencies/${currencyId}`, { creditLimit }),
  // Staff never has Draft filtered out — that's a portal-only restriction
  // (InvoicesController/CreditNotesController's own Draft-visibility rule).
  invoices: (id: string) => api.get<Invoice[]>(`/clients/${id}/invoices`),
  creditNotes: (id: string) => api.get<CreditNote[]>(`/clients/${id}/credit-notes`),
  contacts: (id: string) => api.get<PortalContact[]>(`/clients/${id}/contacts`),
  createContact: (id: string, request: CreatePortalContactRequest) =>
    api.post<PortalContact>(`/clients/${id}/contacts`, request),
  deactivateContact: (id: string, contactId: string) => api.post<void>(`/clients/${id}/contacts/${contactId}/deactivate`),
  reactivateContact: (id: string, contactId: string) => api.post<void>(`/clients/${id}/contacts/${contactId}/reactivate`),
}
