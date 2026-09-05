import { api } from './client'
import type { Invoice } from './types'

// The top-level list is already pinned to the caller's own Client server-side and
// already excludes Draft invoices for a portal caller (InvoicesController.List) — no
// clientId param needed or honored here.
export const invoicesApi = {
  list: () => api.get<Invoice[]>('/invoices'),
}
