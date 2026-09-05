import { api } from './client'
import type { CreateFinancialYearRequest, DebtorsAgingSnapshot, FinancialYear } from './types'

export const financialCalendarApi = {
  listYears: () => api.get<FinancialYear[]>('/financial-years'),
  getYear: (id: string) => api.get<FinancialYear>(`/financial-years/${id}`),
  createYear: (request: CreateFinancialYearRequest) => api.post<FinancialYear>('/financial-years', request),
  closePeriod: (id: string) => api.post<void>(`/financial-periods/${id}/close`),
  debtorsAging: (periodId: string) => api.get<DebtorsAgingSnapshot[]>(`/financial-periods/${periodId}/debtors-aging`),
}
