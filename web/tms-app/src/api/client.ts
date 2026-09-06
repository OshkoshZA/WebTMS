import { getSession, setSession } from './session'

const BASE = '/api/v1'

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function parseErrorMessage(response: Response): Promise<string> {
  try {
    const body = await response.clone().json()
    if (typeof body === 'string') return body
    if (body && typeof body === 'object') {
      if ('detail' in body && typeof body.detail === 'string') return body.detail
      if ('title' in body && typeof body.title === 'string') return body.title
    }
  } catch {
    // not a JSON body — fall through to plain text below.
  }
  const text = await response.text().catch(() => '')
  return text || response.statusText || `Request failed with status ${response.status}`
}

// A refresh triggered by two requests hitting a 401 at once shares one attempt
// rather than racing the backend's refresh-token rotation against itself — a second
// rotation of the same token while the first is still in flight would revoke the
// whole family and force a fresh login for no reason.
let refreshInFlight: Promise<boolean> | null = null

async function refreshAccessToken(): Promise<boolean> {
  const session = getSession()
  if (!session) return false

  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${BASE}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: session.refreshToken }),
      })
      if (!response.ok) {
        setSession(null)
        return false
      }
      const data = await response.json()
      setSession({
        ...session,
        accessToken: data.accessToken,
        accessTokenExpiresAt: data.expiresAt,
        refreshToken: data.refreshToken,
        refreshTokenExpiresAt: data.refreshTokenExpiresAt,
      })
      return true
    } catch {
      return false
    }
  })()

  const result = await refreshInFlight
  refreshInFlight = null
  return result
}

async function request<T>(method: string, path: string, body: unknown, isRetry: boolean): Promise<T> {
  const session = getSession()
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (session) headers.Authorization = `Bearer ${session.accessToken}`

  const response = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (response.status === 401 && session && !isRetry && (await refreshAccessToken())) {
    return request<T>(method, path, body, true)
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseErrorMessage(response))
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string): Promise<T> => request<T>('GET', path, undefined, false),
  post: <T>(path: string, body?: unknown): Promise<T> => request<T>('POST', path, body ?? {}, false),
  put: <T>(path: string, body?: unknown): Promise<T> => request<T>('PUT', path, body ?? {}, false),
  delete: <T>(path: string): Promise<T> => request<T>('DELETE', path, undefined, false),
}

// For an endpoint that returns a file (a PDF, a CSV export) rather than JSON — needs
// the same bearer token as every other call, so a plain <a href> won't work. Fetches
// the bytes directly and triggers a browser download via a Blob object URL, the same
// approach api/audit.ts's own CSV export already established. No 401-retry-on-expired-
// token here, unlike the shared JSON request helper above — a stale token on one of
// these rare, manually-clicked downloads just surfaces a clear error to retry.
export async function downloadFile(path: string, fallbackFileName: string): Promise<void> {
  const session = getSession()
  const response = await fetch(`${BASE}${path}`, {
    headers: session ? { Authorization: `Bearer ${session.accessToken}` } : {},
  })
  if (!response.ok) {
    throw new ApiError(response.status, await parseErrorMessage(response))
  }

  const blob = await response.blob()
  const fileName = response.headers.get('content-disposition')?.match(/filename="?([^"]+)"?/)?.[1] ?? fallbackFileName
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
