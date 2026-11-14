'use client'

import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
  SESSION_STATE_UNAVAILABLE,
} from '@/lib/session-availability'
import {
  isValidSessionIdentityMarker,
  readSessionIdentityMarker,
  writeSessionIdentityChangeMarker,
  writeSessionIdentityMarker,
} from '@/lib/browser-storage-policy'

export const SESSION_RECOVERY_DEADLINE_MS = 8_000

export type IdentityBoundLogoutOutcome =
  | 'logged-out'
  | 'session-missing'
  | 'identity-changed'
  | 'unavailable'
  | 'failed'

interface IdentityBoundLogoutOptions {
  /** Dashboard 已知首屏身份版本时直接使用；根错误边界留空并先探测权威版本。 */
  identityVersion?: string
  deadlineMs?: number
  fetchImpl?: typeof fetch
}

interface RecoverSessionToLoginOptions extends IdentityBoundLogoutOptions {
  announce?: (identityVersion: string) => void
  navigate?: (path: string) => void
  createGeneration?: () => string
}

interface RecoverSessionToLoginWithIdentityChangeOptions extends RecoverSessionToLoginOptions {
  /** CAS 冲突时当前入口必须显式离开旧文档；不得依赖 SessionGuard 的 fetch 副作用。 */
  onIdentityChanged: () => void | Promise<void>
}

export function announceSessionIdentityChange(identityVersion: string) {
  writeSessionIdentityMarker(identityVersion)
}

/** 只广播“身份发生过变化”，不传播可能来自迟到响应的旧版本值。 */
export function signalSessionIdentityChange() {
  try {
    writeSessionIdentityChangeMarker(crypto.randomUUID())
  } catch {
    // 存储不可用时当前标签仍会硬导航；其他标签由聚焦探针自愈。
  }
}

export function storedSessionIdentityVersion(): string | null {
  return readSessionIdentityMarker()
}

/**
 * 仅删除调用文档当前确认过的身份世代。
 *
 * 根错误边界没有 SessionIdentityProvider，必须先从服务端读取权威版本，再带同一版本执行
 * CAS logout。任何网络/存储故障、协议异常或身份竞态都保持 fail-closed：不追逐新版本重试，
 * 也不把仍有效的 cookie 带去 /login 触发 dashboard 重定向环。
 */
export async function requestIdentityBoundLogout(
  options: IdentityBoundLogoutOptions = {},
): Promise<IdentityBoundLogoutOutcome> {
  const fetchImpl = options.fetchImpl ?? globalThis.fetch
  const configuredDeadlineMs = options.deadlineMs
  const deadlineMs = typeof configuredDeadlineMs === 'number' && Number.isFinite(configuredDeadlineMs)
    ? Math.max(1, configuredDeadlineMs)
    : SESSION_RECOVERY_DEADLINE_MS
  const controller = new AbortController()
  const deadlineReached = Symbol('session-recovery-deadline')
  const deadlineAt = Date.now() + deadlineMs
  let deadlineExpired = false
  let timeout: ReturnType<typeof globalThis.setTimeout> | undefined
  const deadlinePromise = new Promise<typeof deadlineReached>((resolve) => {
    timeout = globalThis.setTimeout(() => {
      deadlineExpired = true
      controller.abort()
      resolve(deadlineReached)
    }, deadlineMs)
  })
  const beforeDeadline = async <T>(operation: () => Promise<T>): Promise<T | typeof deadlineReached> => {
    if (deadlineExpired || Date.now() >= deadlineAt) {
      controller.abort()
      return deadlineReached
    }
    return Promise.race([operation(), deadlinePromise])
  }

  try {
    let identityVersion = options.identityVersion
    if (identityVersion !== undefined && !isValidSessionIdentityMarker(identityVersion)) {
      return 'failed'
    }

    if (identityVersion === undefined) {
      const authority = await beforeDeadline(() => fetchImpl(
        '/api/auth/session-identity-version',
        {
          cache: 'no-store',
          signal: controller.signal,
        },
      ))
      if (authority === deadlineReached) return 'failed'
      const authorityState = authority.headers.get(SESSION_STATE_HEADER)
      if (authority.status === 401) {
        return authorityState === null ? 'session-missing' : 'failed'
      }
      if (authority.status === 503) {
        return authorityState === SESSION_STATE_UNAVAILABLE ? 'unavailable' : 'failed'
      }
      if (authority.status !== 200) return 'failed'
      if (authorityState !== null) return 'failed'

      identityVersion = authority.headers.get(SESSION_IDENTITY_VERSION_HEADER) ?? undefined
      if (!isValidSessionIdentityMarker(identityVersion)) return 'failed'
    }

    const logout = await beforeDeadline(() => fetchImpl(
      '/api/auth/logout',
      {
        method: 'POST',
        cache: 'no-store',
        headers: { [SESSION_IDENTITY_VERSION_HEADER]: identityVersion },
        signal: controller.signal,
      },
    ))
    if (logout === deadlineReached) return 'failed'
    const logoutState = logout.headers.get(SESSION_STATE_HEADER)
    if (logout.status === 200) return logoutState === null ? 'logged-out' : 'failed'
    if (
      logout.status === 409
      && logoutState === SESSION_STATE_IDENTITY_CHANGED
      && isValidSessionIdentityMarker(
        logout.headers.get(SESSION_IDENTITY_VERSION_HEADER),
      )
    ) {
      return 'identity-changed'
    }
    if (logout.status === 503) {
      return logoutState === SESSION_STATE_UNAVAILABLE ? 'unavailable' : 'failed'
    }
    return 'failed'
  } catch {
    return 'failed'
  } finally {
    if (timeout !== undefined) globalThis.clearTimeout(timeout)
  }
}

function defaultLogoutGeneration(): string {
  try {
    return crypto.randomUUID()
  } catch {
    return `${Date.now()}-${Math.random()}`
  }
}

/** 成功或服务端已无会话时才广播并 replace 到登录页；其余结果一律停留原页。 */
export async function recoverSessionToLogin(
  options: RecoverSessionToLoginOptions = {},
): Promise<IdentityBoundLogoutOutcome> {
  const outcome = await requestIdentityBoundLogout(options)
  if (outcome === 'identity-changed') {
    // 不采信响应携带的新版本，只广播“发生过变化”；当前标签由各入口明确 reload/replace。
    signalSessionIdentityChange()
    return outcome
  }
  if (outcome !== 'logged-out' && outcome !== 'session-missing') return outcome

  const announce = options.announce ?? announceSessionIdentityChange
  const navigate = options.navigate ?? ((path: string) => window.location.replace(path))
  const generation = options.createGeneration?.() ?? defaultLogoutGeneration()
  try {
    announce(`logged-out-${generation}`)
  } catch {
    // 广播失败不能阻止当前标签离开已确认失效的文档。
  }

  try {
    navigate('/login')
    return outcome
  } catch {
    // 会话已经删除；保留错误页与可重试入口，下一次探测会走 session-missing 分支。
    return 'failed'
  }
}

/**
 * 面向 UI 入口的完整恢复动作：除安全退出外，还强制调用方声明身份竞态时如何离开旧文档。
 * 动作抛错会降级为 failed，让错误边界重新开放重试，而不是静默卡在旧身份页面。
 */
export async function recoverSessionToLoginWithIdentityChange(
  options: RecoverSessionToLoginWithIdentityChangeOptions,
): Promise<IdentityBoundLogoutOutcome> {
  const { onIdentityChanged, ...recoveryOptions } = options
  const outcome = await recoverSessionToLogin(recoveryOptions)
  if (outcome !== 'identity-changed') return outcome

  try {
    await onIdentityChanged()
    return outcome
  } catch {
    return 'failed'
  }
}
