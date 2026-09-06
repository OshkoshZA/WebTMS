import { api } from './client'
import type { AgedDebtorsSummary, CreditExposureSummary, MarginSummary, OnTimeDeliverySummary, PayablesSummary } from './types'

export const dashboardApi = {
  marginSummary: () => api.get<MarginSummary>('/dashboard/margin-summary'),
  creditExposureSummary: () => api.get<CreditExposureSummary>('/dashboard/credit-exposure-summary'),
  payablesSummary: () => api.get<PayablesSummary>('/dashboard/payables-summary'),
  agedDebtorsSummary: () => api.get<AgedDebtorsSummary>('/dashboard/aged-debtors-summary'),
  onTimeDeliverySummary: () => api.get<OnTimeDeliverySummary>('/dashboard/on-time-delivery-summary'),
}
