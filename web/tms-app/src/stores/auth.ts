import { defineStore } from 'pinia'
import { api } from '../api/client'
import { getSession, hasFunction, setSession, type Session } from '../api/session'

interface LoginResponse {
  accessToken: string
  expiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  tenantId: string
  companyId: string
  roles: string[]
  functions: string[]
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    session: getSession() as Session | null,
  }),
  getters: {
    isAuthenticated: (state) => state.session !== null,
    email: (state) => state.session?.email ?? '',
    roles: (state) => state.session?.roles ?? [],
  },
  actions: {
    async login(email: string, password: string) {
      const data = await api.post<LoginResponse>('/auth/login', { email, password })
      const session: Session = {
        accessToken: data.accessToken,
        accessTokenExpiresAt: data.expiresAt,
        refreshToken: data.refreshToken,
        refreshTokenExpiresAt: data.refreshTokenExpiresAt,
        tenantId: data.tenantId,
        companyId: data.companyId,
        roles: data.roles,
        functions: data.functions,
        email,
      }
      setSession(session)
      this.session = session
    },

    async logout() {
      const session = getSession()
      if (session) {
        // Best-effort: the local session is cleared either way, even if this call
        // fails (network down, token already expired) — logout must never get stuck.
        await api.post('/auth/logout', { refreshToken: session.refreshToken }).catch(() => undefined)
      }
      setSession(null)
      this.session = null
    },

    // Delegates to the framework-free session module (see its own doc comment) so
    // both this store and api/client.ts's 401-retry logic read one shared source.
    hasFunction,
  },
})
