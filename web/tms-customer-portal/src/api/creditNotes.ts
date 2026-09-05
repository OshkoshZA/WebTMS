import { api } from './client'
import type { CreditNote } from './types'

export const creditNotesApi = {
  list: () => api.get<CreditNote[]>('/credit-notes'),
}
