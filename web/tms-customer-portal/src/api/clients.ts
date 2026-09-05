import { api } from './client'
import type { CreditStatus } from './types'

export const clientsApi = {
  creditStatus: (clientId: string) => api.get<CreditStatus>(`/clients/${clientId}/credit-status`),
}
