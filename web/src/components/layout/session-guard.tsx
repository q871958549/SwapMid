'use client'

import { useLayoutEffect } from 'react'
import {
  BACKGROUND_REQUEST_HEADER,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
  SESSION_STATE_UNAVAILABLE,
  buildSessionExpiredPath,
  buildSessionUnavailablePath,
} from '@/lib/session-availability'
import {
  announceSessionIdentityChange,
  signalSessionIdentityChange,
  storedSessionIdentityVersion,
} from '@/lib/client-session-recovery'
import {
  isValidSessionIdentityMarker,
  SESSION_IDENTITY_CHANGE_MARKER_KEY,
  SESSION_IDENTITY_MARKER_KEY,
} from '@/lib/browser-storage-policy'

export {
  announceSessionIdentityChange,
  signalSessionIdentityChange,
} from '@/lib/client-session-recovery'

function isBackgroundRequest([input, init]: Parameters<typeof fetch>): boolean {
  const headers = new Headers(input instanceof Request ? input.headers : undefined)
  if (init?.headers) new Headers(init.headers).forEach((value, name) => headers.set(name, value))
  return headers.get(BACKGROUND_REQUEST_HEADER) === '1'
}

function getSameOriginUrl(input: RequestInfo | URL): URL | null {
  try {
    const raw = typeof input === 'string'
      ? input
      : input instanceof URL
        ? input.href
        : input.url
    const current = new URL(window.location.href)
    const target = new URL(raw, current)
    return target.origin === current.origin ? target : null
  } catch {
    return null
  }
}

type SameOriginSessionPath = 'proxy' | 'api' | 'auth-control'

function getSameOriginSessionPath(input: RequestInfo | URL): SameOriginSessionPath | null {
  const target = getSameOriginUrl(input)
  if (!target) return null
  if (
    target.pathname === '/api/auth/logout'
    || target.pathname === '/api/auth/session-identity-version'
  ) {
    return 'auth-control'
  }
  if (target.pathname === '/api/proxy' || target.pathname.startsWith('/api/proxy/')) {
    return 'proxy'
  }
  if (target.pathname.startsWith('/api/')) {
    return 'api'
  }
  return null
}

interface SessionGuardFetchRuntime {
  baseFetch: typeof fetch
  dispatcher: typeof fetch
  active: { token: symbol, handler: typeof fetch } | null
}

const SESSION_GUARD_FETCH_RUNTIME = Symbol.for('swapmid.session-guard.fetch-runtime.v1')

function getSessionGuardFetchRuntime(): SessionGuardFetchRuntime {
  const existing = Reflect.get(window, SESSION_GUARD_FETCH_RUNTIME) as
    | SessionGuardFetchRuntime
    | undefined
  if (existing) {
    if (window.fetch === existing.baseFetch) window.fetch = existing.dispatcher
    return existing
  }

  const runtime: SessionGuardFetchRuntime = {
    baseFetch: window.fetch,
    dispatcher: undefined as unknown as typeof fetch,
    active: null,
  }
  runtime.dispatcher = async (...args: Parameters<typeof fetch>) => {
    const active = runtime.active
    return active
      ? active.handler(...args)
      : runtime.baseFetch(...args)
  }
  Reflect.set(window, SESSION_GUARD_FETCH_RUNTIME, runtime)
  window.fetch = runtime.dispatcher
  return runtime
}

function withIdentityVersion(
  args: Parameters<typeof fetch>,
  identityVersion: string,
): Parameters<typeof fetch> {
  const [input, init] = args
  if (!getSameOriginUrl(input)) return args
  if (getSameOriginSessionPath(input) === 'auth-control') return args

  const headers = new Headers(input instanceof Request ? input.headers : undefined)
  if (init?.headers) {
    new Headers(init.headers).forEach((value, name) => headers.set(name, value))
  }
  headers.set(SESSION_IDENTITY_VERSION_HEADER, identityVersion)
  return [input, { ...init, headers }]
}

export function SessionGuard({ identityVersion }: { identityVersion: string }) {
  useLayoutEffect(() => {
    const runtime = getSessionGuardFetchRuntime()
    const generationToken = Symbol(identityVersion)
    const original = runtime.baseFetch
    let disposed = false
    let reloading = false
    const hardReload = () => {
      if (reloading) return
      reloading = true
      window.location.reload()
    }
    const broadcastIdentityChangeAndReload = () => {
      if (reloading) return
      reloading = true
      signalSessionIdentityChange()
      window.location.reload()
    }
    const guardedFetch: typeof fetch = async (...args: Parameters<typeof fetch>) => {
      const sameOrigin = getSameOriginUrl(args[0]) !== null
      const sessionPath = getSameOriginSessionPath(args[0])
      const background = isBackgroundRequest(args)
      const res = await original(...withIdentityVersion(args, identityVersion))
      try {
        if (!disposed && !background && sameOrigin && sessionPath !== 'auth-control') {
          const returnTo = location.pathname + location.search
          if (
            res.status === 409
            && res.headers.get(SESSION_STATE_HEADER) === SESSION_STATE_IDENTITY_CHANGED
          ) {
            broadcastIdentityChangeAndReload()
          } else if (res.status === 401 && sessionPath === 'proxy') {
            window.location.href = buildSessionExpiredPath(
              returnTo,
              identityVersion,
            )
          } else if (
            res.status === 503
            && res.headers.get(SESSION_STATE_HEADER) === SESSION_STATE_UNAVAILABLE
          ) {
            window.location.href = buildSessionUnavailablePath(returnTo)
          }
        }
      } catch {
        // 拦截判断绝不影响正常响应返回
      }
      return res
    }
    runtime.active = { token: generationToken, handler: guardedFetch }

    let reconciliationInFlight = false
    let urgentReconciliationPending = false
    let authorityEpoch = 0
    let lastAuthorityCheckAt = 0
    const reconcileIdentityVersion = async (
      forceAuthorityCheck = false,
      urgent = false,
    ) => {
      if (!forceAuthorityCheck && storedSessionIdentityVersion() === identityVersion) return
      const now = Date.now()
      if (reconciliationInFlight) {
        if (urgent) urgentReconciliationPending = true
        return
      }
      if (
        forceAuthorityCheck
        && !urgent
        && now - lastAuthorityCheckAt < 5_000
      ) {
        return
      }
      reconciliationInFlight = true
      lastAuthorityCheckAt = now
      const startedEpoch = authorityEpoch
      try {
        const response = await original('/api/auth/session-identity-version', {
          cache: 'no-store',
        })
        if (disposed || startedEpoch !== authorityEpoch) return
        const returnTo = window.location.pathname + window.location.search
        const authorityState = response.headers.get(SESSION_STATE_HEADER)
        if (response.status === 401) {
          if (authorityState === null) {
            window.location.href = buildSessionExpiredPath(
              returnTo,
              identityVersion,
            )
          }
          return
        }
        if (response.status === 503) {
          if (authorityState === SESSION_STATE_UNAVAILABLE) {
            window.location.href = buildSessionUnavailablePath(returnTo)
          }
          return
        }
        if (response.status !== 200) {
          return
        }
        if (authorityState !== null) {
          return
        }

        const currentVersion = response.headers.get(SESSION_IDENTITY_VERSION_HEADER)
        if (!isValidSessionIdentityMarker(currentVersion)) {
          return
        }
        if (reloading) return
        announceSessionIdentityChange(currentVersion)
        if (currentVersion !== identityVersion) hardReload()
      } catch {
        // 网络瞬断时不制造刷新风暴；所有业务请求仍受服务端版本前置条件保护。
      } finally {
        reconciliationInFlight = false
        if (!disposed && urgentReconciliationPending) {
          urgentReconciliationPending = false
          void reconcileIdentityVersion(true, true)
        }
      }
    }
    void reconcileIdentityVersion()
    const onStorage = (event: StorageEvent) => {
      if (
        (
          event.key === SESSION_IDENTITY_MARKER_KEY
          && isValidSessionIdentityMarker(event.newValue)
          && event.newValue !== identityVersion
        )
        || (
          event.key === SESSION_IDENTITY_CHANGE_MARKER_KEY
          && isValidSessionIdentityMarker(event.newValue)
        )
      ) {
        authorityEpoch += 1
        void reconcileIdentityVersion(true, true)
      }
    }
    const reconcileAfterVisibilityChange = () => {
      void reconcileIdentityVersion(true)
    }
    const onPageShow = () => reconcileAfterVisibilityChange()
    const onFocus = () => reconcileAfterVisibilityChange()
    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') reconcileAfterVisibilityChange()
    }
    window.addEventListener('storage', onStorage)
    window.addEventListener('pageshow', onPageShow)
    window.addEventListener('focus', onFocus)
    document.addEventListener('visibilitychange', onVisibilityChange)

    return () => {
      disposed = true
      if (runtime.active?.token === generationToken) runtime.active = null
      window.removeEventListener('storage', onStorage)
      window.removeEventListener('pageshow', onPageShow)
      window.removeEventListener('focus', onFocus)
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [identityVersion])

  return null
}
