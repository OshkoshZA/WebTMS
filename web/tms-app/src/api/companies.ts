import { api } from './client'
import type { Company, UpdateCompanyRequest } from './types'

export const companiesApi = {
  get: (id: string) => api.get<Company>(`/companies/${id}`),
  update: (id: string, request: UpdateCompanyRequest) => api.put<void>(`/companies/${id}`, request),
}
