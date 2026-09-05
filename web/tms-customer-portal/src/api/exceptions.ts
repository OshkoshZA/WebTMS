import { api } from './client'
import type { ExceptionRecord } from './types'

// ExceptionsController.List/Get pins a Customer Portal contact's own view to
// exceptions tied to their own Client — a credit override raised directly against
// it, or a Debrief exception on one of their own loads. Read-only here: Acknowledge/
// Resolve require exception.manage, never granted to a portal contact.
export const exceptionsApi = {
  list: (status?: number) => api.get<ExceptionRecord[]>(`/exceptions${status !== undefined ? `?status=${status}` : ''}`),
}
