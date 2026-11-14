export const SESSION_RETRY_AFTER_SECONDS = 2
export const SESSION_STATE_HEADER = 'X-SwapMid-Session-State'
export const SESSION_STATE_UNAVAILABLE = 'unavailable'
export const SESSION_STATE_IDENTITY_CHANGED = 'identity-changed'
export const SESSION_STATE_MISSING = 'missing'
export const SESSION_UNAVAILABLE_CODE = 'SESSION_UNAVAILABLE'
export const SESSION_IDENTITY_CHANGED_CODE = 'SESSION_IDENTITY_CHANGED'
export const SESSION_IDENTITY_PRECONDITION_CODE = 'SESSION_IDENTITY_PRECONDITION_REQUIRED'
export const SESSION_IDENTITY_VERSION_HEADER = 'X-SwapMid-Identity-Version'
export const SESSION_IDENTITY_VERSION_QUERY = '_swapmidIdentityVersion'
/**
 * 旁路请求标记（角标、使用量上报这类用户没在等的请求）：SessionGuard 不因它们的任何响应发起会话导航
 * （401 回登录、503 故障页、409 身份变化重建都不触发）。
 * 旁路请求自己去导航只会和退出 / 身份切换流程抢导航（2026-11-04 CI 冒烟实锤：退出时在途的角标请求
 * 401 → 二次导航打断退出）。安全性不受影响：它照样带身份版本头，过期身份会被服务端 409 拒绝、拿不到数据；
 * 会话失效或身份变化由用户的下一次真实请求、跨标签页广播与聚焦核对收敛。只在客户端读取，鉴权代理不转发它。
 */
export const BACKGROUND_REQUEST_HEADER = 'X-SwapMid-Background-Request'

export interface SessionIdentitySearch {
  identityVersions: string[]
  /**
   * 除内部身份参数外的原始查询串。保留参数顺序、重复项及百分号编码，
   * 避免 URLSearchParams 重建后把 `%20` 改为 `+` 等代理语义漂移。
   */
  forwardedSearch: string
}

export function sessionUnavailableHeaders(
  retryAfterSeconds = SESSION_RETRY_AFTER_SECONDS,
): Record<string, string> {
  return {
    'Cache-Control': 'private, no-store',
    'Retry-After': String(Math.max(1, Math.ceil(retryAfterSeconds))),
    [SESSION_STATE_HEADER]: SESSION_STATE_UNAVAILABLE,
  }
}

export function sessionMissingHeaders(): Record<string, string> {
  return {
    'Cache-Control': 'private, no-store',
    [SESSION_STATE_HEADER]: SESSION_STATE_MISSING,
  }
}

export function sessionIdentityChangedHeaders(
  identityVersion: string,
): Record<string, string> {
  return {
    'Cache-Control': 'private, no-store',
    [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
    [SESSION_IDENTITY_VERSION_HEADER]: identityVersion,
  }
}

export function buildSessionExpiredPath(
  returnTo: string,
  expectedIdentityVersion: string,
): string {
  return '/api/auth/session-expired'
    + `?redirect=${encodeURIComponent(returnTo)}`
    + `&${SESSION_IDENTITY_VERSION_QUERY}=${encodeURIComponent(expectedIdentityVersion)}`
}

export function buildSessionUnavailablePath(returnTo: string): string {
  return `/api/auth/session-unavailable?redirect=${encodeURIComponent(returnTo)}`
}

function decodeQueryPart(value: string): string | null {
  try {
    return decodeURIComponent(value.replaceAll('+', ' '))
  } catch {
    return null
  }
}

/**
 * 提取仅供 web 会话围栏使用的查询参数，同时逐字保留其余查询串。
 * `null` 表示查询键或值含非法百分号编码，调用方应拒绝请求。
 */
export function extractSessionIdentitySearch(
  search: string,
): SessionIdentitySearch | null {
  const raw = search.startsWith('?') ? search.slice(1) : search
  if (!raw) return { identityVersions: [], forwardedSearch: '' }

  const identityVersions: string[] = []
  const forwardedParts: string[] = []
  for (const part of raw.split('&')) {
    const equalsAt = part.indexOf('=')
    const rawKey = equalsAt >= 0 ? part.slice(0, equalsAt) : part
    const rawValue = equalsAt >= 0 ? part.slice(equalsAt + 1) : ''
    const key = decodeQueryPart(rawKey)
    const value = decodeQueryPart(rawValue)
    if (key === null || value === null) return null

    if (key === SESSION_IDENTITY_VERSION_QUERY) {
      identityVersions.push(value)
    } else {
      forwardedParts.push(part)
    }
  }

  return {
    identityVersions,
    forwardedSearch: forwardedParts.length > 0 ? `?${forwardedParts.join('&')}` : '',
  }
}

/**
 * `<img>`、`<a download>`、window.open 等原生 GET 无法附加自定义请求头，
 * 用非敏感的身份世代查询参数补齐同一前置条件。Route Handler 校验后会剥离该参数再回源。
 */
export function bindSessionIdentityToUrl(
  url: string,
  identityVersion: string,
): string {
  if (!url.startsWith('/') || url.startsWith('//')) {
    throw new Error('identity-bound URL must be a same-origin absolute path')
  }
  const hashIndex = url.indexOf('#')
  const beforeHash = hashIndex >= 0 ? url.slice(0, hashIndex) : url
  const hash = hashIndex >= 0 ? url.slice(hashIndex) : ''
  const queryIndex = beforeHash.indexOf('?')
  const pathname = queryIndex >= 0 ? beforeHash.slice(0, queryIndex) : beforeHash
  const search = queryIndex >= 0 ? beforeHash.slice(queryIndex) : ''
  const extracted = extractSessionIdentitySearch(search)
  if (!extracted) throw new Error('identity-bound URL has an invalid query string')

  const separator = extracted.forwardedSearch ? '&' : '?'
  return `${pathname}${extracted.forwardedSearch}${separator}${SESSION_IDENTITY_VERSION_QUERY}=${encodeURIComponent(identityVersion)}${hash}`
}
