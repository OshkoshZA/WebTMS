import { api } from './client'
import type { SupplierInvoice } from './types'

// SupplierInvoicesController.List pins the result to the caller's own SubcontractorId
// server-side; Create/Match/Dispute are staff-only (finance.subcontractorinvoice.process)
// so this portal only ever reads.
export const supplierInvoicesApi = {
  list: () => api.get<SupplierInvoice[]>('/supplier-invoices'),
}
