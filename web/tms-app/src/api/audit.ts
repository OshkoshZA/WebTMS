import { api, ApiError } from './client'
import { getSession } from './session'
import type { AuditEntry } from './types'

export interface AuditEntryFilters {
  entityType?: string
  entityId?: string
  companyId?: string
  changedByUserId?: string
  action?: number
  from?: string
  to?: string
  take?: number
}

function buildQuery(filters: AuditEntryFilters): string {
  const params = new URLSearchParams()
  if (filters.entityType) params.set('entityType', filters.entityType)
  if (filters.entityId) params.set('entityId', filters.entityId)
  if (filters.companyId) params.set('companyId', filters.companyId)
  if (filters.changedByUserId) params.set('changedByUserId', filters.changedByUserId)
  if (filters.action !== undefined) params.set('action', String(filters.action))
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  if (filters.take !== undefined) params.set('take', String(filters.take))
  return params.toString()
}

export const auditEntriesApi = {
  list: (filters: AuditEntryFilters) => {
    const qs = buildQuery(filters)
    return api.get<AuditEntry[]>(`/audit-entries${qs ? `?${qs}` : ''}`)
  },
}

// The export endpoint returns a CSV file, not JSON, and needs the same bearer token
// as every other call — a plain <a href> would hit it unauthenticated, so this fetches
// it directly (bypassing api/client.ts's JSON-only request helper) and triggers a
// browser download via a Blob object URL. No 401-retry-on-expired-token here, unlike
// the shared client — a stale token on this rare, manually-clicked action just surfaces
// a clear error to retry, rather than adding refresh-retry complexity for one button.
export async function downloadAuditExport(filters: AuditEntryFilters): Promise<void> {
  const session = getSession()
  const qs = buildQuery(filters)
  const response = await fetch(`/api/v1/audit-entries/export${qs ? `?${qs}` : ''}`, {
    headers: session ? { Authorization: `Bearer ${session.accessToken}` } : {},
  })
  if (!response.ok) {
    throw new ApiError(response.status, await response.text().catch(() => 'Export failed.'))
  }

  const blob = await response.blob()
  const fileName = response.headers.get('content-disposition')?.match(/filename="?([^"]+)"?/)?.[1] ?? 'audit-trail.csv'
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
