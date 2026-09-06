import { api } from './client'
import type { CreditExposureSummary, MarginSummary, PayablesSummary } from './types'

export const dashboardApi = {
  marginSummary: () => api.get<MarginSummary>('/dashboard/margin-summary'),
  creditExposureSummary: () => api.get<CreditExposureSummary>('/dashboard/credit-exposure-summary'),
  payablesSummary: () => api.get<PayablesSummary>('/dashboard/payables-summary'),
}
