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
    subcontractorId: (state) => state.session?.subcontractorId ?? '',
  },
  actions: {
    async login(email: string, password: string) {
      const data = await api.post<LoginResponse>('/auth/login', { email, password })

      // The one field this login response never carries — see api/jwt.ts's own doc
      // comment. A login response with no subcontractor_id claim means this credential
      // isn't a Supplier Portal contact at all (e.g. an internal staff account), which
      // this app has no use for.
      const claims = decodeJwtPayload(data.accessToken)
      const subcontractorId = typeof claims.subcontractor_id === 'string' ? claims.subcontractor_id : null
      if (!subcontractorId) {
        throw new Error('This account is not a Supplier Portal contact.')
      }

      const session: Session = {
        accessToken: data.accessToken,
        accessTokenExpiresAt: data.expiresAt,
        refreshToken: data.refreshToken,
        refreshTokenExpiresAt: data.refreshTokenExpiresAt,
        tenantId: data.tenantId,
        companyId: data.companyId,
        subcontractorId,
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
