import { api } from './client'
import type {
  AcknowledgeConfirmationRequest,
  Debrief,
  LoadConfirmation,
  SubcontractorLeg,
  SubmitDebriefRequest,
} from './types'

// There is no standalone GET /legs/{id} leg-detail route anywhere in the API — the
// leg's own fields (sequence, status, buy amount/currency, confirmation) come entirely
// from the list response below; a leg detail screen just finds its row in that list.
export const legsApi = {
  listForSubcontractor: (subcontractorId: string) =>
    api.get<SubcontractorLeg[]>(`/subcontractors/${subcontractorId}/legs`),
  getConfirmation: (legId: string) => api.get<LoadConfirmation>(`/legs/${legId}/confirmation`),
  acknowledgeConfirmation: (legId: string, request: AcknowledgeConfirmationRequest) =>
    api.post<void>(`/legs/${legId}/confirmation/acknowledge`, request),
  getDebrief: (legId: string) => api.get<Debrief>(`/legs/${legId}/debrief`),
  submitDebrief: (legId: string, request: SubmitDebriefRequest) =>
    api.post<Debrief>(`/legs/${legId}/debrief`, request),
}
