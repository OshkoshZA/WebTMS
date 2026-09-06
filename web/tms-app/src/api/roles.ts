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

// There's no server-side "list portal-safe roles" endpoint (§13.1) — a Role fit to
// hand to a Client/Subcontractor portal contact is one whose every function starts
// with the given prefix (portal.client. / portal.subcontractor.), and carries at
// least one, since ClientContactsController/SubcontractorContactsController's own
// Create action hard-rejects anything else.
export function portalSafeRoles(roles: Role[], prefix: string): Role[] {
  return roles.filter((r) => r.functions.length > 0 && r.functions.every((f) => f.code.startsWith(prefix)))
}
