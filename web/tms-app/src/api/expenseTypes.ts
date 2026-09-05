import { api } from './client'
import type { ExpenseType } from './types'

export interface CreateExpenseTypeRequest {
  code: string
  name: string
}

export type UpdateExpenseTypeRequest = CreateExpenseTypeRequest

export const expenseTypesApi = {
  list: () => api.get<ExpenseType[]>('/expense-types'),
  get: (id: string) => api.get<ExpenseType>(`/expense-types/${id}`),
  create: (request: CreateExpenseTypeRequest) => api.post<ExpenseType>('/expense-types', request),
  update: (id: string, request: UpdateExpenseTypeRequest) => api.put<void>(`/expense-types/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/expense-types/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/expense-types/${id}/reactivate`),
}
