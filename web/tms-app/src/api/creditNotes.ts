import { api } from './client'
import type { CreateCreditNoteRequest, CreditNote } from './types'

export const creditNotesApi = {
  list: (clientId?: string) => api.get<CreditNote[]>(`/credit-notes${clientId ? `?clientId=${clientId}` : ''}`),
  get: (id: string) => api.get<CreditNote>(`/credit-notes/${id}`),
  create: (request: CreateCreditNoteRequest) => api.post<CreditNote>('/credit-notes', request),
  issue: (id: string, issueDate?: string) => api.post<void>(`/credit-notes/${id}/issue`, { issueDate }),
  void: (id: string) => api.post<void>(`/credit-notes/${id}/void`),
}
