import { api } from './client'
import type { SubcontractorAccrual } from './types'

export const accrualsApi = {
  list: (subcontractorId?: string, status?: number) => {
    const params = new URLSearchParams()
    if (subcontractorId) params.set('subcontractorId', subcontractorId)
    if (status !== undefined) params.set('status', String(status))
    const qs = params.toString()
    return api.get<SubcontractorAccrual[]>(`/accruals${qs ? `?${qs}` : ''}`)
  },
  get: (id: string) => api.get<SubcontractorAccrual>(`/accruals/${id}`),
}
