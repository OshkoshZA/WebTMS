import { api } from './client'
import type { AcknowledgeConfirmationRequest, Debrief, LoadConfirmation, SubmitDebriefRequest } from './types'

// LegsController's own top-level /legs/{id}/... routes (distinct from /loads/{id}/legs/...
// used for creation/allocation) — same endpoints the Supplier Portal calls for its own
// contact, but here reached by an internal staff user standing in for a carrier who
// called or emailed instead of using the portal themselves.
export const legsApi = {
  getConfirmation: (legId: string) => api.get<LoadConfirmation>(`/legs/${legId}/confirmation`),
  acknowledgeConfirmation: (legId: string, request: AcknowledgeConfirmationRequest) =>
    api.post<void>(`/legs/${legId}/confirmation/acknowledge`, request),
  getDebrief: (legId: string) => api.get<Debrief>(`/legs/${legId}/debrief`),
  submitDebrief: (legId: string, request: SubmitDebriefRequest) => api.post<Debrief>(`/legs/${legId}/debrief`, request),
}
