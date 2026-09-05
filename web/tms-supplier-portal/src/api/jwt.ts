// A portal contact's own SubcontractorId never appears in the LoginResponse body (unlike
// tms-app's, which carries TenantId/CompanyId/Roles/Functions directly) — JwtTokenService
// only puts it in the token itself, as a "subcontractor_id" claim. This is a plain,
// unverified decode for display/routing purposes only — the token is verified
// server-side on every request regardless of what this reads client-side.
export function decodeJwtPayload(token: string): Record<string, unknown> {
  const payload = token.split('.')[1]
  if (!payload) return {}
  const base64 = payload.replace(/-/g, '+').replace(/_/g, '/')
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=')
  try {
    return JSON.parse(atob(padded)) as Record<string, unknown>
  } catch {
    return {}
  }
}
