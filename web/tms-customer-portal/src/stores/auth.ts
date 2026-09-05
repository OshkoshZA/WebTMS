import { defineStore } from 'pinia'
import { api } from '../api/client'
import { decodeJwtPayload } from '../api/jwt'
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
    clientId: (state) => state.session?.clientId ?? '',
  },
  actions: {
    async login(email: string, password: string) {
      const data = await api.post<LoginResponse>('/auth/login', { email, password })

      // The one field this login response never carries — see api/jwt.ts's own doc
      // comment. A login response with no portal_client_id claim means this
      // credential isn't a Customer Portal contact at all (e.g. an internal staff
      // account), which this app has no use for.
      const claims = decodeJwtPayload(data.accessToken)
      const clientId = typeof claims.portal_client_id === 'string' ? claims.portal_client_id : null
      if (!clientId) {
        throw new Error('This account is not a Customer Portal contact.')
      }

      const session: Session = {
        accessToken: data.accessToken,
        accessTokenExpiresAt: data.expiresAt,
        refreshToken: data.refreshToken,
        refreshTokenExpiresAt: data.refreshTokenExpiresAt,
        tenantId: data.tenantId,
        companyId: data.companyId,
        clientId,
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
        await api.post('/auth/logout', { refreshToken: session.refreshToken }).catch(() => undefined)
      }
      setSession(null)
      this.session = null
    },

    hasFunction,
  },
})
