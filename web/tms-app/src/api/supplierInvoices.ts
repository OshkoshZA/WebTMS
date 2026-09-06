import { api } from './client'
import type { CreateSupplierInvoiceRequest, MatchSupplierInvoiceResponse, SupplierInvoice } from './types'

export const supplierInvoicesApi = {
  list: (subcontractorId?: string) => api.get<SupplierInvoice[]>(`/supplier-invoices${subcontractorId ? `?subcontractorId=${subcontractorId}` : ''}`),
  get: (id: string) => api.get<SupplierInvoice>(`/supplier-invoices/${id}`),
  create: (request: CreateSupplierInvoiceRequest) => api.post<SupplierInvoice>('/supplier-invoices', request),
  match: (id: string, accrualIds: string[]) =>
    api.post<MatchSupplierInvoiceResponse>(`/supplier-invoices/${id}/match`, { accrualIds }),
  dispute: (id: string, reason: string) => api.post<void>(`/supplier-invoices/${id}/dispute`, { reason }),
}
