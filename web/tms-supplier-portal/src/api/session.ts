// The one place a logged-in session actually lives — deliberately framework-free (no
// Pinia/Vue import here) so api/client.ts can read/refresh it without a circular
// dependency on the Pinia store that wraps it for components (stores/auth.ts).
// Persisted to localStorage so a page reload doesn't force a fresh login.

export interface Session {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  tenantId: string
  companyId: string
  subcontractorId: string
  roles: string[]
  functions: string[]
  email: string
}

const STORAGE_KEY = 'tms.supplier-portal.session'

function loadFromStorage(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Session) : null
  } catch {
    return null
  }
}

let current: Session | null = loadFromStorage()

export function getSession(): Session | null {
  return current
}

export function setSession(session: Session | null): void {
  current = session
  try {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // localStorage unavailable (private browsing, quota, etc.) — the session still
    // works for the lifetime of this tab, it just won't survive a reload.
  }
}

export function hasFunction(code: string): boolean {
  return current?.functions.includes(code) ?? false
}
