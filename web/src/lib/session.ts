import { cookies, headers } from 'next/headers'
import { getSessionCookieName } from './site-instance'
import {
  storeSession,
  getStoredSession,
  deleteStoredSessionIfUnchanged,
  invalidateStoredSessionIfUnchanged,
  getRefreshLock,
  setRefreshLock,
  clearRefreshLock,
  isValidSessionId,
  sessionRevision,
  type SessionLookupResult,
  type ValidSessionResult,
} from './session-store'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_RETRY_AFTER_SECONDS,
} from './session-availability'
import {
  redirectSessionFailure,
  redirectSessionIdentityChanged,
} from './session-navigation'
import {
  createSessionIdentityVersion,
  getSessionIdentityVersion,
} from './session-identity'
import {
  createSessionRefreshWaitDeadline,
  forceJoinSessionRefresh,
  refreshSession,
  waitForSessionRefreshDeadline,
  type SessionRefreshResult,
} from './session-refresh'
import {
  SessionIdentityChangedError,
  startImpersonationForSessionId,
  stopImpersonationForSessionId,
  type ImpersonationTarget,
} from './session-identity-mutation'
import type { FullSession } from './session-types'
import { SESSION_ABSOLUTE_TTL_MS } from './session-lifetime'

export type { FullSession, SessionPayload } from './session-types'
export {
  SessionIdentityChangedError,
  type ImpersonationTarget,
} from './session-identity-mutation'


// Refresh a little before the access token actually expires.
const REFRESH_SKEW_MS = 60 * 1000

const COOKIE_SECURE = process.env.COOKIE_SECURE !== undefined
  ? process.env.COOKIE_SECURE !== 'false'
  : process.env.NODE_ENV === 'production'

const COOKIE_OPTS = {
  httpOnly: true,
  secure: COOKIE_SECURE,
  sameSite: 'lax' as const,
  maxAge: SESSION_ABSOLUTE_TTL_MS / 1000,
  path: '/',
}

// 偏好 cookie（swapmid_theme / swapmid_font_size / swapmid_language）：**非 httpOnly**——根 layout 服务端读它做首屏
// 直出防闪白，客户端 ThemeProvider 也要读写。生命周期独立于 session（登出不清，未登录的登录页也需正确主题）。
const PREF_COOKIE_OPTS = {
  httpOnly: false,
  secure: COOKIE_SECURE,
  sameSite: 'lax' as const,
  maxAge: 365 * 24 * 60 * 60,
  path: '/',
}

function sessionIdFromCookie(value: string | undefined): string | undefined {
  return value !== undefined && isValidSessionId(value) ? value : undefined
}

export async function createSession(payload: FullSession) {
  const sessionId = crypto.randomUUID() // 36 bytes — well under 4096
  const stored: FullSession = {
    ...payload,
    identityVersion: createSessionIdentityVersion(),
    issuedAt: payload.issuedAt ?? Date.now(),
  }
  await storeSession(sessionId, stored)
  const cookieStore = await cookies()
  cookieStore.set(getSessionCookieName(), sessionId, COOKIE_OPTS)
  // 把账号偏好镜像进 cookie，供根 layout 首屏直出（防主题闪白）+ 客户端 ThemeProvider 初值。
  if (payload.theme) cookieStore.set('swapmid_theme', payload.theme, PREF_COOKIE_OPTS)
  if (payload.fontSize) cookieStore.set('swapmid_font_size', String(payload.fontSize), PREF_COOKIE_OPTS)
  if (payload.language) cookieStore.set('swapmid_language', payload.language, PREF_COOKIE_OPTS)
  return stored
}

export async function getSessionResult(): Promise<SessionLookupResult> {
  const cookieStore = await cookies()
  const sid = sessionIdFromCookie(cookieStore.get(getSessionCookieName())?.value)
  if (!sid) return { status: 'missing' }
  return await getStoredSession(sid)
}

/**
 * 旧页面的安全兼容层：missing 仍返回 null；unavailable 必须去 503，
 * 绝不能折叠成 null 后误走 session-expired 删除 cookie。
 */
export async function getSession(): Promise<FullSession | null> {
  const result = await getSessionResult()
  if (result.status === 'found') {
    const requestHeaders = await headers()
    const expectedIdentityVersion = requestHeaders.get(
      SESSION_IDENTITY_VERSION_HEADER,
    )
    if (
      expectedIdentityVersion !== null
      && expectedIdentityVersion !== getSessionIdentityVersion(result.session)
    ) {
      return redirectSessionIdentityChanged(
        requestHeaders.get('x-swapmid-pathname') ?? undefined,
      )
    }
    return result.session
  }
  if (result.status === 'missing') return null
  return redirectSessionFailure(result)
}

/**
 * Returns a session whose access token is still valid, transparently refreshing
 * it via the OpenIddict refresh_token grant when it is expired or about to expire.
 *
 * The ABP access token lives ~1h while the server-side session store idles 8h
 * (absolute cap 7d). Refresh also slides that idle window. Without this a still-
 * "logged-in" session would send an expired Bearer token and every backend call
 * would 401. Concurrent refreshes for the same session are deduped because
 * OpenIddict rotates refresh tokens (one-time-use).
 */
export async function getValidSessionResult(): Promise<ValidSessionResult> {
  const cookieStore = await cookies()
  const sid = sessionIdFromCookie(cookieStore.get(getSessionCookieName())?.value)
  if (!sid) return { status: 'missing' }

  const lookup = await getStoredSession(sid)
  if (lookup.status !== 'found') return lookup

  const result = await resolveValidSession(sid, lookup.session)
  if (result.status !== 'found') return result

  // 真实消费会话的位置再做一次版本核对，关闭 Proxy 前置读取与 RSC/Server Action
  // 实际执行之间的 TOCTOU 窗口。完整文档导航没有该头，会按当前身份重建整棵 UI。
  const expectedIdentityVersion = (await headers()).get(
    SESSION_IDENTITY_VERSION_HEADER,
  )
  if (
    expectedIdentityVersion !== null
    && expectedIdentityVersion !== getSessionIdentityVersion(result.session)
  ) {
    return {
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session: result.session,
    }
  }

  return result
}

async function resolveValidSession(
  sid: string,
  session: FullSession,
): Promise<ValidSessionResult> {
  // Still valid → use as-is.
  if (Date.now() < session.accessTokenExpiresAt - REFRESH_SKEW_MS) {
    return { status: 'found', session }
  }

  // 模拟态令牌到期：grant 强制不发 refresh_token（设计如此，见蓝图 §3.1），故到点即无法自行续期。
  // 自动切回管理员原会话，但当前请求必须停在这里：若继续递归并返回 found，
  // 目标账号页面刚发出的 POST/DELETE 就会被静默改用管理员 token 重放，构成提权。
  // 恢复后返回独立 identity-changed，交由 Route Handler 409 / RSC 硬导航重建身份树。
  if (!session.refreshToken && session.impersonator) {
    try {
      const restored = await stopImpersonationForSessionId(
        sid,
        getSessionIdentityVersion(session),
      )
      if (!restored) return { status: 'missing' }
      return {
        status: 'identity-changed',
        reason: 'impersonation-expired',
        session: restored,
      }
    } catch (error) {
      if (error instanceof SessionIdentityChangedError) {
        return resolveConcurrentSessionChange(sid, session)
      }
      console.error('[session] restore impersonator session failed')
      return unavailableResult()
    }
  }

  // Expired/near-expiry but no refresh token → nothing we can do; caller will 401.
  if (!session.refreshToken) return { status: 'found', session }

  return coordinateRefreshWithL1(sid, session, false)
}

async function coordinateRefreshWithL1(
  sid: string,
  session: FullSession,
  forceJoin: boolean,
  waitDeadline?: number,
): Promise<ValidSessionResult> {
  const forceDeadline = forceJoin
    ? waitDeadline ?? createSessionRefreshWaitDeadline()
    : undefined
  const inFlight = getRefreshLock(sid)
  if (inFlight) {
    if (!forceJoin) return inFlight

    const localWait = await waitForSessionRefreshDeadline(
      inFlight,
      forceDeadline!,
    )
    if (localWait.status === 'timed-out') return unavailableResult()
    const localResult = localWait.value
    if (localResult.status !== 'unavailable') return localResult
    if (Date.now() >= forceDeadline!) return localResult

    // 普通刷新可能刚把远端 lease 写成 cooldown。只 compare-delete 我们观察到的
    // Promise，再沿用最初 deadline 升级为 forced join；新的 L1 owner 不会被误删。
    clearRefreshLock(sid, inFlight)
    return coordinateRefreshWithL1(sid, session, true, forceDeadline)
  }

  const refreshPromise = refreshAccessToken(
    sid,
    session,
    forceJoin,
    forceDeadline,
  )
  setRefreshLock(sid, refreshPromise)
  try {
    return await refreshPromise
  } finally {
    clearRefreshLock(sid, refreshPromise)
  }
}

function unavailableResult(): SessionLookupResult {
  return {
    status: 'unavailable',
    retryAfterSeconds: SESSION_RETRY_AFTER_SECONDS,
  }
}

async function refreshAccessToken(
  sid: string,
  session: FullSession,
  forceJoin: boolean,
  waitDeadline?: number,
): Promise<ValidSessionResult> {
  let result: SessionRefreshResult
  try {
    result = forceJoin
      ? await forceJoinSessionRefresh(sid, session, waitDeadline)
      : await refreshSession(sid, session)
  } catch {
    console.error('[session] refresh coordination unavailable')
    return unavailableResult()
  }
  if (result.status === 'updated') {
    return { status: 'found', session: result.session }
  }
  if (result.status === 'changed') {
    return resolveConcurrentSessionChange(sid, session)
  }
  return result.status === 'missing' ? result : unavailableResult()
}

function forceJoinValidSession(
  sid: string,
  session: FullSession,
): Promise<ValidSessionResult> {
  if (!session.refreshToken) return Promise.resolve(unavailableResult())
  return coordinateRefreshWithL1(sid, session, true)
}

async function resolveConcurrentSessionChange(
  sid: string,
  previous: FullSession,
): Promise<ValidSessionResult> {
  const latest = await getStoredSession(sid)
  if (latest.status !== 'found') return latest

  if (
    getSessionIdentityVersion(latest.session) !== getSessionIdentityVersion(previous)
    || latest.session.userName !== previous.userName
  ) {
    return {
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session: latest.session,
    }
  }

  // 同一身份只是在另一实例先完成了 token 轮换：使用胜者的新会话继续，不能误登出。
  return resolveValidSession(sid, latest.session)
}

export type DeleteSessionResult =
  | { status: 'deleted' }
  | { status: 'identity-changed'; identityVersion: string }
  | { status: 'unavailable'; retryAfterSeconds: number }

export type InvalidateSessionResult =
  | { status: 'invalidated' }
  | { status: 'identity-changed'; session: FullSession }
  | { status: 'session-updated'; session: FullSession }
  | { status: 'unavailable'; retryAfterSeconds: number }

/**
 * 后端 401 只能淘汰“实际发起该请求”的那版会话。身份切换或并发 token 刷新已经
 * 写入新 revision 时返回冲突，让调用方整页重建，绝不能把胜者会话误删。
 */
export async function invalidateSessionIfUnchanged(
  expected: FullSession,
): Promise<InvalidateSessionResult> {
  const sid = sessionIdFromCookie((await cookies()).get(getSessionCookieName())?.value)
  if (!sid) return { status: 'invalidated' }

  try {
    const deletion = await invalidateStoredSessionIfUnchanged(
      sid,
      sessionRevision(expected),
    )
    if (deletion === 'updated' || deletion === 'missing') {
      return { status: 'invalidated' }
    }
    if (deletion === 'refreshing') {
      const refreshed = await forceJoinValidSession(sid, expected)
      if (refreshed.status === 'missing') return { status: 'invalidated' }
      if (refreshed.status === 'unavailable') return refreshed
      if (refreshed.status === 'identity-changed') {
        return { status: 'identity-changed', session: refreshed.session }
      }
      return getSessionIdentityVersion(refreshed.session)
        !== getSessionIdentityVersion(expected)
        ? { status: 'identity-changed', session: refreshed.session }
        : { status: 'session-updated', session: refreshed.session }
    }
  } catch {
    console.error('[session] conditional invalidation unavailable')
    return {
      status: 'unavailable',
      retryAfterSeconds: SESSION_RETRY_AFTER_SECONDS,
    }
  }

  const latest = await getStoredSession(sid)
  if (latest.status === 'missing') return { status: 'invalidated' }
  if (latest.status === 'unavailable') return latest

  return getSessionIdentityVersion(latest.session)
    !== getSessionIdentityVersion(expected)
    ? { status: 'identity-changed', session: latest.session }
    : { status: 'session-updated', session: latest.session }
}

export async function deleteSession(
  expectedIdentityVersion: string,
): Promise<DeleteSessionResult> {
  const sid = sessionIdFromCookie((await cookies()).get(getSessionCookieName())?.value)
  if (!sid) return { status: 'deleted' }

  // 浏览器 Cookie 无法 CAS，退出响应绝不写 Set-Cookie；否则迟到响应会抹掉另一标签
  // 刚完成的新登录。这里只条件删除请求捕获的 Redis revision，stale SID 由 /login
  // 的服务端状态复核放行，并在下次登录时自然覆盖。
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const currentResult = await getStoredSession(sid)
    if (currentResult.status === 'missing') return { status: 'deleted' }
    if (currentResult.status === 'unavailable') return currentResult

    const current = currentResult.session
    const actualIdentityVersion = getSessionIdentityVersion(current)
    if (actualIdentityVersion !== expectedIdentityVersion) {
      return {
        status: 'identity-changed',
        identityVersion: actualIdentityVersion,
      }
    }
    try {
      const deletion = await deleteStoredSessionIfUnchanged(
        sid,
        sessionRevision(current),
      )
      if (deletion === 'conflict') continue
      return { status: 'deleted' }
    } catch {
      console.error('[session] logout session cleanup unavailable')
      return {
        status: 'unavailable',
        retryAfterSeconds: SESSION_RETRY_AFTER_SECONDS,
      }
    }
  }

  // 连续 CAS 冲突说明会话仍在高速变化；宁可不退出也不误退新身份。
  const finalResult = await getStoredSession(sid)
  if (finalResult.status === 'missing') return { status: 'deleted' }
  if (finalResult.status === 'unavailable') return finalResult
  const finalIdentityVersion = getSessionIdentityVersion(finalResult.session)
  if (finalIdentityVersion !== expectedIdentityVersion) {
    return {
      status: 'identity-changed',
      identityVersion: finalIdentityVersion,
    }
  }
  throw new Error('SESSION_CONCURRENT_MUTATION')
}

/**
 * 模拟登录：把当前会话完整打包存进 impersonator 快照，顶层换成目标用户身份。
 * 同一个 sid（cookie 不动，浏览器无感）；`POST /api/auth/start-impersonation`
 * 换到目标 token 后调用本函数落地。sid/cookie 读写全封装在本文件内，调用方不接触。
 */
export async function startImpersonation(
  target: ImpersonationTarget,
  expectedIdentityVersion: string,
): Promise<FullSession> {
  const cookieStore = await cookies()
  const sid = sessionIdFromCookie(cookieStore.get(getSessionCookieName())?.value)
  if (!sid) throw new Error('SESSION_MISSING')
  return startImpersonationForSessionId(sid, target, expectedIdentityVersion)
}

/**
 * 切回：把 impersonator 快照搬回顶层，清空模拟态。幂等——本就没在模拟时原样返回当前会话。
 * 原 token 若已过期也无妨：恢复后下一次 getValidSessionResult 会用其 refreshToken 自动续期。
 */
export async function stopImpersonation(
  expectedIdentityVersion: string,
): Promise<FullSession | null> {
  const cookieStore = await cookies()
  const sid = sessionIdFromCookie(cookieStore.get(getSessionCookieName())?.value)
  if (!sid) return null
  return stopImpersonationForSessionId(sid, expectedIdentityVersion)
}
