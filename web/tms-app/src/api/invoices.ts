import { api } from './client'
import type { GenerateInvoiceRequest, Invoice } from './types'

export const invoicesApi = {
  list: (clientId?: string) => api.get<Invoice[]>(`/invoices${clientId ? `?clientId=${clientId}` : ''}`),
  get: (id: string) => api.get<Invoice>(`/invoices/${id}`),
  generate: (request: GenerateInvoiceRequest) => api.post<Invoice>('/invoices/generate', request),
  issue: (id: string, issueDate?: string) => api.post<void>(`/invoices/${id}/issue`, { issueDate }),
  void: (id: string) => api.post<void>(`/invoices/${id}/void`),
}
