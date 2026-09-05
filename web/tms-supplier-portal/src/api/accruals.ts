import { api } from './client'
import type { SubcontractorAccrual } from './types'

// AccrualsController.List pins the result to the caller's own SubcontractorId
// server-side regardless of any subcontractorId query param, so none is sent here.
export const accrualsApi = {
  list: () => api.get<SubcontractorAccrual[]>('/accruals'),
}
