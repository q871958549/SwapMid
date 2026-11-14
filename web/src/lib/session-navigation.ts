import { redirect } from 'next/navigation'
import type { SessionLookupResult } from './session-store'
import { isSafeInternalPath } from './safe-internal-path'
import { SESSION_IDENTITY_VERSION_QUERY } from './session-availability'

export type SessionFailure = Exclude<
  SessionLookupResult,
  { status: 'found' }
>

/**
 * 会话失败的唯一导航映射：
 * - missing 才清 cookie 并重新登录；
 * - unavailable 保留 cookie，落到真正的 503 重试页。
 */
export function redirectSessionFailure(
  failure: SessionFailure,
  returnTo?: string,
  expectedIdentityVersion?: string,
): never {
  const endpoint = failure.status === 'missing'
    ? '/api/auth/session-expired'
    : '/api/auth/session-unavailable'
  const search: string[] = []
  if (isSafeInternalPath(returnTo)) {
    search.push(`redirect=${encodeURIComponent(returnTo)}`)
  }
  if (failure.status === 'missing' && expectedIdentityVersion) {
    search.push(
      `${SESSION_IDENTITY_VERSION_QUERY}=${encodeURIComponent(expectedIdentityVersion)}`,
    )
  }
  const location = search.length > 0 ? `${endpoint}?${search.join('&')}` : endpoint

  redirect(location)
}

/**
 * 服务端渲染期间发现模拟身份已自动结束时，必须经独立 Route Handler 做一次整页往返。
 * 不能在当前 RSC 请求里继续取数，也不能把它当 missing 清掉仍有效的管理员会话。
 */
export function redirectSessionIdentityChanged(returnTo?: string): never {
  const endpoint = '/api/auth/session-identity-changed'
  const location = isSafeInternalPath(returnTo)
    ? `${endpoint}?redirect=${encodeURIComponent(returnTo)}`
    : endpoint

  redirect(location)
}
