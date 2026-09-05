import { api } from './client'
import type { ExceptionRecord } from './types'

// ExceptionsController.List/Get pins a Supplier Portal contact's own view to
// exceptions tied to their own Subcontractor — a Debrief exception on one of their own
// legs, or an accrual/invoice variance on one of their own matched SupplierInvoices.
// Read-only here: Acknowledge/Resolve require exception.manage, never granted to a
// portal contact.
export const exceptionsApi = {
  list: (status?: number) => api.get<ExceptionRecord[]>(`/exceptions${status !== undefined ? `?status=${status}` : ''}`),
}
