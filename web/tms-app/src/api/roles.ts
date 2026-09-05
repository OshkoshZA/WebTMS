import { api } from './client'
import type { AppFunction, CreateRoleRequest, Role } from './types'

export const rolesApi = {
  list: () => api.get<Role[]>('/roles'),
  get: (id: string) => api.get<Role>(`/roles/${id}`),
  create: (request: CreateRoleRequest) => api.post<Role>('/roles', request),
  // Incremental grant/revoke, one function at a time — RolesController has no bulk
  // "replace the whole function set" endpoint despite what the design doc's own
  // §11.2 table says.
  grantFunction: (id: string, functionId: string) => api.post<void>(`/roles/${id}/functions`, { functionId }),
  revokeFunction: (id: string, functionId: string) => api.delete<void>(`/roles/${id}/functions/${functionId}`),
}

export const functionsApi = {
  list: () => api.get<AppFunction[]>('/functions'),
}
