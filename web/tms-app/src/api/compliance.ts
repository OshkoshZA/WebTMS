import { api } from './client'

export const complianceApi = {
  reconcileExceptions: () => api.post<void>('/compliance/reconcile-exceptions'),
}
