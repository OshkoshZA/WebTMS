import { api } from './client'
import type { AddCompanyRoleRequest, CreateUserRequest, UpdateUserRequest, User, UserCompanyRole } from './types'

export const usersApi = {
  list: () => api.get<User[]>('/users'),
  get: (id: string) => api.get<User>(`/users/${id}`),
  create: (request: CreateUserRequest) => api.post<User>('/users', request),
  update: (id: string, request: UpdateUserRequest) => api.put<void>(`/users/${id}`, request),
  deactivate: (id: string) => api.post<void>(`/users/${id}/deactivate`),
  reactivate: (id: string) => api.post<void>(`/users/${id}/reactivate`),
  addCompanyRole: (id: string, request: AddCompanyRoleRequest) =>
    api.post<UserCompanyRole>(`/users/${id}/company-roles`, request),
  removeCompanyRole: (id: string, companyRoleId: string) => api.delete<void>(`/users/${id}/company-roles/${companyRoleId}`),
}
