import 'server-only'

import {
  createSessionRefreshWaitDeadline,
  forceJoinSessionRefresh,
  waitForSessionRefreshDeadline,
  type SessionRefreshResult,
} from './session-refresh'
import {
  clearRefreshLock,
  getRefreshLock,
  getStoredSession,
  replaceIdentitySessionIfUnchanged,
  sessionRevision,
} from './session-store'
import {
  createSessionIdentityVersion,
  getSessionIdentityVersion,
} from './session-identity'
import type { FullSession, SessionPayload } from './session-types'

const IDENTITY_MUTATION_MAX_ATTEMPTS = 3

export interface ImpersonationTarget extends SessionPayload {
  accessToken: string
  accessTokenExpiresAt: number
}

export class SessionIdentityChangedError extends Error {
  constructor(public readonly identityVersion: string) {
    super('SESSION_IDENTITY_CHANGED')
    this.name = 'SessionIdentityChangedError'
  }
}

function assertExpectedIdentity(
  current: FullSession,
  expectedIdentityVersion: string,
): void {
  const actual = getSessionIdentityVersion(current)
  if (actual !== expectedIdentityVersion) {
    throw new SessionIdentityChangedError(actual)
  }
}

async function readCurrentSession(sid: string): Promise<FullSession> {
  const lookup = await getStoredSession(sid)
  if (lookup.status === 'missing') throw new Error('SESSION_MISSING')
  if (lookup.status === 'unavailable') throw new Error('SESSION_UNAVAILABLE')
  return lookup.session
}

async function latestAfterForcedJoin(
  sid: string,
  current: FullSession,
): Promise<FullSession> {
  if (!current.refreshToken) {
    throw new Error('SESSION_REFRESH_INVARIANT')
  }

  const waitDeadline = createSessionRefreshWaitDeadline()
  let localOwner = getRefreshLock(sid)
  while (localOwner) {
    const localWait = await waitForSessionRefreshDeadline(
      localOwner,
      waitDeadline,
    )
    if (localWait.status === 'timed-out') {
      throw new Error('SESSION_UNAVAILABLE')
    }
    const localResult = localWait.value
    if (localResult.status === 'found' || localResult.status === 'identity-changed') {
      return localResult.session
    }
    if (localResult.status === 'missing') throw new Error('SESSION_MISSING')
    if (Date.now() >= waitDeadline) throw new Error('SESSION_UNAVAILABLE')

    // 普通 L1 刷新可能刚写入 cooldown 后返回 unavailable；不能把该结果当成
    // forced join 的最终结论。仅清自己观察到的 Promise，再检查是否已有新 owner。
    clearRefreshLock(sid, localOwner)
    localOwner = getRefreshLock(sid)
  }

  let result: SessionRefreshResult
  try {
    result = await forceJoinSessionRefresh(sid, current, waitDeadline)
  } catch {
    throw new Error('SESSION_UNAVAILABLE')
  }
  if (result.status === 'updated') return result.session
  if (result.status === 'changed') return readCurrentSession(sid)
  if (result.status === 'missing') throw new Error('SESSION_MISSING')
  throw new Error('SESSION_UNAVAILABLE')
}

async function throwFinalMutationConflict(
  sid: string,
  expectedIdentityVersion: string,
): Promise<never> {
  const latest = await readCurrentSession(sid)
  assertExpectedIdentity(latest, expectedIdentityVersion)
  throw new Error('SESSION_CONCURRENT_MUTATION')
}

export async function startImpersonationForSessionId(
  sid: string,
  target: ImpersonationTarget,
  expectedIdentityVersion: string,
): Promise<FullSession> {
  let current = await readCurrentSession(sid)

  for (let attempt = 0; attempt < IDENTITY_MUTATION_MAX_ATTEMPTS; attempt += 1) {
    assertExpectedIdentity(current, expectedIdentityVersion)
    if (current.impersonator) throw new Error('IMPERSONATION_ALREADY_ACTIVE')

    const next: FullSession = {
      ...target,
      identityVersion: createSessionIdentityVersion(),
      refreshToken: undefined,
      impersonator: {
        userId: current.userId,
        userName: current.userName,
        name: current.name,
        grantedPolicies: current.grantedPolicies,
        identityVersion: getSessionIdentityVersion(current),
        accessToken: current.accessToken,
        refreshToken: current.refreshToken,
        accessTokenExpiresAt: current.accessTokenExpiresAt,
      },
    }
    const update = await replaceIdentitySessionIfUnchanged(
      sid,
      sessionRevision(current),
      next,
    )
    if (update === 'updated') return next
    if (update === 'missing') throw new Error('SESSION_MISSING')
    current = update === 'refreshing'
      ? await latestAfterForcedJoin(sid, current)
      : await readCurrentSession(sid)
  }

  return throwFinalMutationConflict(sid, expectedIdentityVersion)
}

export async function stopImpersonationForSessionId(
  sid: string,
  expectedIdentityVersion: string,
): Promise<FullSession | null> {
  let current: FullSession
  try {
    current = await readCurrentSession(sid)
  } catch (error) {
    if (error instanceof Error && error.message === 'SESSION_MISSING') return null
    throw error
  }

  for (let attempt = 0; attempt < IDENTITY_MUTATION_MAX_ATTEMPTS; attempt += 1) {
    assertExpectedIdentity(current, expectedIdentityVersion)
    if (!current.impersonator) return current

    const restored: FullSession = {
      ...current.impersonator,
      identityVersion: createSessionIdentityVersion(),
    }
    const update = await replaceIdentitySessionIfUnchanged(
      sid,
      sessionRevision(current),
      restored,
    )
    if (update === 'updated') return restored
    if (update === 'missing') return null
    // 模拟态顶层没有 refreshToken。lease 若仍在，forced join 会立刻抛
    // SESSION_REFRESH_INVARIANT，切回被卡死在模拟壳里。这里只重读再 CAS，
    // 等 lease 过期或源不匹配被 Lua 清掉；有 refreshToken 的管理员会话仍走 join。
    current = update === 'refreshing' && current.refreshToken
      ? await latestAfterForcedJoin(sid, current)
      : await readCurrentSession(sid)
  }

  return throwFinalMutationConflict(sid, expectedIdentityVersion)
}
