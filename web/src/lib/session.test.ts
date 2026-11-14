import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FullSession } from './session'

const navigationMocks = vi.hoisted(() => ({
  redirect: vi.fn((location: string) => {
    throw new Error(`NEXT_REDIRECT:${location}`)
  }),
}))
const refreshProtocolMocks = vi.hoisted(() => ({
  acquireSessionRefreshLease: vi.fn(),
  settleSessionRefreshInvalidGrant: vi.fn(),
  settleSessionRefreshSuccess: vi.fn(),
  settleSessionRefreshUnavailable: vi.fn(),
}))

vi.mock('next/navigation', () => navigationMocks)
vi.mock('next/headers', () => ({
  cookies: vi.fn(),
  headers: vi.fn(),
}))
vi.mock('./session-store', () => ({
  SESSION_STORE_COMMAND_TIMEOUT_MS: 1_500,
  storeSession: vi.fn(),
  getStoredSession: vi.fn(),
  deleteStoredSession: vi.fn(),
  deleteStoredSessionIfUnchanged: vi.fn(),
  invalidateStoredSessionIfUnchanged: vi.fn(),
  replaceIdentitySessionIfUnchanged: vi.fn(),
  getRefreshLock: vi.fn(),
  setRefreshLock: vi.fn(),
  clearRefreshLock: vi.fn(),
  isValidSessionId: vi.fn(() => true),
  sessionRevision: vi.fn((session: FullSession) => ({
    identityVersion: session.identityVersion ?? '',
    userName: session.userName,
    accessToken: session.accessToken,
    refreshToken: session.refreshToken ?? '',
    accessTokenExpiresAt: session.accessTokenExpiresAt,
  })),
}))
vi.mock('./session-refresh-protocol', () => refreshProtocolMocks)

vi.spyOn(console, 'error').mockImplementation(() => {})

import { cookies, headers } from 'next/headers'
import {
  clearRefreshLock,
  deleteStoredSession,
  deleteStoredSessionIfUnchanged,
  getRefreshLock,
  getStoredSession,
  invalidateStoredSessionIfUnchanged,
  isValidSessionId,
  replaceIdentitySessionIfUnchanged,
  setRefreshLock,
} from './session-store'
import {
  deleteSession,
  getSession,
  getSessionResult,
  getValidSessionResult,
  invalidateSessionIfUnchanged,
  startImpersonation,
  stopImpersonation,
} from './session'

const cookiesMock = vi.mocked(cookies)
const headersMock = vi.mocked(headers)
const getStoredSessionMock = vi.mocked(getStoredSession)
const deleteStoredSessionMock = vi.mocked(deleteStoredSession)
const deleteStoredSessionIfUnchangedMock = vi.mocked(deleteStoredSessionIfUnchanged)
const invalidateStoredSessionIfUnchangedMock = vi.mocked(
  invalidateStoredSessionIfUnchanged,
)
const replaceIdentitySessionIfUnchangedMock = vi.mocked(
  replaceIdentitySessionIfUnchanged,
)
const isValidSessionIdMock = vi.mocked(isValidSessionId)
const getRefreshLockMock = vi.mocked(getRefreshLock)
const setRefreshLockMock = vi.mocked(setRefreshLock)
const clearRefreshLockMock = vi.mocked(clearRefreshLock)
const SID = 'swapmid_sid'

const baseSession: FullSession = {
  userId: 'user-admin-id',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: { 'SwapMid.Foo': true },
  identityVersion: 'identity-admin-v1',
  accessToken: 'tok-old',
  refreshToken: 'refresh-old',
  accessTokenExpiresAt: 0,
}

const cookieDeleteMock = vi.fn()

function setCookie(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) => value !== undefined && name === SID ? { value } : undefined,
    set: vi.fn(),
    delete: cookieDeleteMock,
  } as never)
}

function found(session: FullSession) {
  return { status: 'found' as const, session }
}

function expiredSession(overrides: Partial<FullSession> = {}): FullSession {
  return {
    ...baseSession,
    accessTokenExpiresAt: Date.now() - 1_000,
    ...overrides,
  }
}

function revisionOf(session: FullSession) {
  return {
    identityVersion: session.identityVersion ?? '',
    userName: session.userName,
    accessToken: session.accessToken,
    refreshToken: session.refreshToken ?? '',
    accessTokenExpiresAt: session.accessTokenExpiresAt,
  }
}

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  headersMock.mockResolvedValue(new Headers() as never)
  isValidSessionIdMock.mockReturnValue(true)
  getRefreshLockMock.mockReturnValue(undefined)
  replaceIdentitySessionIfUnchangedMock.mockResolvedValue('updated')
  deleteStoredSessionIfUnchangedMock.mockResolvedValue('updated')
  invalidateStoredSessionIfUnchangedMock.mockResolvedValue('updated')
  deleteStoredSessionMock.mockResolvedValue()
  refreshProtocolMocks.acquireSessionRefreshLease.mockResolvedValue({
    status: 'owner',
    lease: {
      ownerId: 'owner-1',
      fence: 1,
      source: 'source-1',
    },
  })
  refreshProtocolMocks.settleSessionRefreshSuccess.mockResolvedValue('updated')
  refreshProtocolMocks.settleSessionRefreshInvalidGrant.mockResolvedValue('updated')
  refreshProtocolMocks.settleSessionRefreshUnavailable.mockResolvedValue('updated')
})

afterEach(() => {
  vi.useRealTimers()
})

describe('会话查找三态', () => {
  it('没有 swapmid_sid 时返回 missing，不读存储', async () => {
    setCookie(undefined)

    await expect(getSessionResult()).resolves.toEqual({ status: 'missing' })
    await expect(getValidSessionResult()).resolves.toEqual({ status: 'missing' })
    expect(getStoredSessionMock).not.toHaveBeenCalled()
  })

  it('存储 missing 原样透传，兼容 getSession 仍返回 null', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({ status: 'missing' })

    await expect(getSessionResult()).resolves.toEqual({ status: 'missing' })
    await expect(getSession()).resolves.toBeNull()
  })

  it('存储 unavailable 原样透传，兼容 getSession 去 503 而不伪装过期', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })

    await expect(getSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    await expect(getSession()).rejects.toThrow(
      'NEXT_REDIRECT:/api/auth/session-unavailable',
    )
    expect(navigationMocks.redirect).toHaveBeenCalledWith('/api/auth/session-unavailable')
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
  })

  it('RSC 请求头身份世代不一致时 getSession 拒绝把新身份注入旧文档', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(baseSession))
    headersMock.mockResolvedValue(new Headers({
      'X-SwapMid-Identity-Version': 'identity-stale-v0',
      'x-swapmid-pathname': '/dashboard',
    }) as never)

    await expect(getSession()).rejects.toThrow(
      'NEXT_REDIRECT:/api/auth/session-identity-changed?redirect=%2Fdashboard',
    )
    expect(navigationMocks.redirect).toHaveBeenCalledWith(
      '/api/auth/session-identity-changed?redirect=%2Fdashboard',
    )
  })
})

describe('有效会话与模拟态恢复', () => {
  it('未到刷新阈值直接返回 found，不刷新、不取锁', async () => {
    setCookie('sid-1')
    const session = { ...baseSession, accessTokenExpiresAt: Date.now() + 3_600_000 }
    getStoredSessionMock.mockResolvedValue(found(session))

    await expect(getValidSessionResult()).resolves.toEqual(found(session))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(getRefreshLockMock).not.toHaveBeenCalled()
  })

  it('RSC 请求头身份世代过旧时 getValidSessionResult fail-closed', async () => {
    setCookie('sid-1')
    const session = { ...baseSession, accessTokenExpiresAt: Date.now() + 3_600_000 }
    getStoredSessionMock.mockResolvedValue(found(session))
    headersMock.mockResolvedValue(new Headers({
      'X-SwapMid-Identity-Version': 'identity-stale-v0',
    }) as never)

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session,
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('已过期但没有 refreshToken 仍返回 found，交由后端判 401', async () => {
    setCookie('sid-1')
    const session = expiredSession({ refreshToken: undefined })
    getStoredSessionMock.mockResolvedValue(found(session))

    await expect(getValidSessionResult()).resolves.toEqual(found(session))
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('模拟态令牌过期后以新世代写回原身份，并返回 identity-changed 阻断当前请求', async () => {
    setCookie('sid-1')
    const impersonator = {
      ...baseSession,
      accessToken: 'admin-token',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const session = expiredSession({
      userName: 'target',
      refreshToken: undefined,
      impersonator,
    })
    getStoredSessionMock.mockResolvedValue(found(session))

    const result = await getValidSessionResult()

    expect(result.status).toBe('identity-changed')
    if (result.status !== 'identity-changed') throw new Error('expected identity-changed')
    expect(result.reason).toBe('impersonation-expired')
    expect(result.session).toEqual(expect.objectContaining({
      userId: 'user-admin-id',
      userName: 'admin',
      accessToken: 'admin-token',
    }))
    expect(result.session.identityVersion).toEqual(expect.any(String))
    expect(result.session.identityVersion).not.toBe(session.identityVersion)
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(session),
      result.session,
    )
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('模拟态恢复时 key 已消失返回 missing，绝不重建', async () => {
    setCookie('sid-1')
    const impersonator = {
      ...baseSession,
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const session = expiredSession({
      refreshToken: undefined,
      impersonator,
    })
    getStoredSessionMock.mockResolvedValue(found(session))
    replaceIdentitySessionIfUnchangedMock.mockResolvedValue('missing')

    await expect(getValidSessionResult()).resolves.toEqual({ status: 'missing' })
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(session),
      expect.objectContaining({ userName: 'admin' }),
    )
  })

  it('模拟态恢复写存储失败返回 unavailable', async () => {
    setCookie('sid-1')
    const impersonator = {
      ...baseSession,
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const session = expiredSession({
      refreshToken: undefined,
      impersonator,
    })
    getStoredSessionMock.mockResolvedValue(found(session))
    replaceIdentitySessionIfUnchangedMock.mockRejectedValue(new Error('redis down'))

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
  })

  it('模拟态恢复 CAS 冲突时返回存储中的最新身份，不覆盖并发切换', async () => {
    setCookie('sid-1')
    const session = expiredSession({
      userName: 'target',
      refreshToken: undefined,
      impersonator: {
        ...baseSession,
        accessToken: 'admin-token',
        accessTokenExpiresAt: Date.now() + 3_600_000,
      },
    })
    const latest = {
      ...baseSession,
      identityVersion: 'identity-newer-v2',
      accessToken: 'newer-token',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(session))
      .mockResolvedValueOnce(found(session))
      .mockResolvedValueOnce(found(latest))
      .mockResolvedValueOnce(found(latest))
    replaceIdentitySessionIfUnchangedMock.mockResolvedValue('conflict')

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session: latest,
    })
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledOnce()
  })
})

describe('refresh token 刷新分类', () => {
  it('刷新成功写回并返回 found，始终释放进程内锁', async () => {
    setCookie('sid-1')
    const expired = expiredSession()
    getStoredSessionMock.mockResolvedValue(found(expired))
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        access_token: 'tok-new',
        refresh_token: 'refresh-new',
        expires_in: 3_600,
      }),
    } as Response)

    const result = await getValidSessionResult()

    expect(result.status).toBe('found')
    if (result.status !== 'found') throw new Error('expected found')
    expect(result.session).toEqual(expect.objectContaining({
      userName: 'admin',
      accessToken: 'tok-new',
      refreshToken: 'refresh-new',
    }))
    expect(refreshProtocolMocks.settleSessionRefreshSuccess).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(expired),
      {
        ownerId: 'owner-1',
        fence: 1,
        source: 'source-1',
      },
      expect.objectContaining({ accessToken: 'tok-new', refreshToken: 'refresh-new' }),
    )
    expect(setRefreshLockMock).toHaveBeenCalledWith('sid-1', expect.any(Promise))
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', expect.any(Promise))

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/connect/token')
    expect(String(init.body)).toContain('grant_type=refresh_token')
    expect(String(init.body)).toContain('refresh_token=refresh-old')
  })

  it('响应未轮换 refresh_token 时沿用旧值', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'tok-new', expires_in: 3_600 }),
    } as Response)

    const result = await getValidSessionResult()

    expect(result.status).toBe('found')
    if (result.status === 'found') {
      expect(result.session.refreshToken).toBe('refresh-old')
    }
  })

  it('已有刷新锁时复用同一个三态 Promise', async () => {
    setCookie('sid-1')
    const lockedSession = { ...baseSession, accessToken: 'tok-from-lock' }
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    getRefreshLockMock.mockReturnValue(Promise.resolve(found(lockedSession)))

    await expect(getValidSessionResult()).resolves.toEqual(found(lockedSession))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(setRefreshLockMock).not.toHaveBeenCalled()
  })

  it('跨进程败者只做 waiter，胜者写回后按 changed 复用新会话且不二次消费 token', async () => {
    setCookie('sid-1')
    const expired = expiredSession()
    const winner = {
      ...expired,
      accessToken: 'tok-winner',
      refreshToken: 'refresh-winner',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(expired))
      .mockResolvedValueOnce(found(winner))
    refreshProtocolMocks.acquireSessionRefreshLease
      .mockResolvedValueOnce({ status: 'waiter', retryAfterMs: 1 })
      .mockResolvedValueOnce({ status: 'changed' })

    await expect(getValidSessionResult()).resolves.toEqual(found(winner))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(refreshProtocolMocks.settleSessionRefreshInvalidGrant).not.toHaveBeenCalled()
  })

  it('只有当前 owner 的结构化 invalid_grant 才能 fenced 删除该 source', async () => {
    setCookie('sid-1')
    const expired = expiredSession()
    getStoredSessionMock.mockResolvedValue(found(expired))
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({ error: 'invalid_grant' }),
    } as Response)

    await expect(getValidSessionResult()).resolves.toEqual({ status: 'missing' })
    expect(refreshProtocolMocks.settleSessionRefreshInvalidGrant).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(expired),
      {
        ownerId: 'owner-1',
        fence: 1,
        source: 'source-1',
      },
    )
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', expect.any(Promise))
  })

  it('刷新写回 CAS 冲突时复用同身份并发刷新胜者，不以旧 token 覆盖', async () => {
    setCookie('sid-1')
    const expired = expiredSession()
    const winner = {
      ...expired,
      accessToken: 'tok-winner',
      refreshToken: 'refresh-winner',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(expired))
      .mockResolvedValueOnce(found(winner))
    refreshProtocolMocks.settleSessionRefreshSuccess.mockResolvedValue('conflict')
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        access_token: 'tok-loser',
        refresh_token: 'refresh-loser',
        expires_in: 3_600,
      }),
    } as Response)

    await expect(getValidSessionResult()).resolves.toEqual(found(winner))
    expect(refreshProtocolMocks.settleSessionRefreshSuccess).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(expired),
      expect.objectContaining({ ownerId: 'owner-1', fence: 1 }),
      expect.objectContaining({ accessToken: 'tok-loser' }),
    )
  })

  it('invalid_grant 的 CAS 删除冲突时返回最新身份，不删除并发切换结果', async () => {
    setCookie('sid-1')
    const expired = expiredSession()
    const latest = {
      ...baseSession,
      identityVersion: 'identity-target-v2',
      userName: 'target',
      accessToken: 'target-token',
      refreshToken: undefined,
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(expired))
      .mockResolvedValueOnce(found(latest))
    refreshProtocolMocks.settleSessionRefreshInvalidGrant.mockResolvedValue('conflict')
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({ error: 'invalid_grant' }),
    } as Response)

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session: latest,
    })
    expect(refreshProtocolMocks.settleSessionRefreshInvalidGrant).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(expired),
      expect.objectContaining({ ownerId: 'owner-1', fence: 1 }),
    )
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
  })

  it.each([
    { status: 400, body: 'invalid_grant' },
    { status: 429, body: JSON.stringify({ error: 'slow_down' }) },
    { status: 500, body: JSON.stringify({ error: 'server_error' }) },
  ])('HTTP $status 不能证明会话死亡，返回 unavailable 且不删除', async ({ status, body }) => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({
      ok: false,
      status,
      text: async () => body,
    } as Response)

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
  })

  it('网络异常返回 unavailable，保留会话并释放锁', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockRejectedValue(new Error('network down'))

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', expect.any(Promise))
  })

  it('刷新端点黑洞在 8 秒 deadline 后返回 unavailable，不堆积请求', async () => {
    vi.useFakeTimers()
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockImplementation((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        const signal = init.signal
        if (!signal) throw new Error('refresh request must have an AbortSignal')
        signal.addEventListener('abort', () => reject(signal.reason), { once: true })
      }))

    const resultPromise = getValidSessionResult()
    await vi.advanceTimersByTimeAsync(8_000)

    await expect(resultPromise).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', expect.any(Promise))
  })

  it.each([
    { label: '非 JSON', json: () => Promise.reject(new SyntaxError('bad json')) },
    { label: '缺 access_token', json: () => Promise.resolve({ expires_in: 3_600 }) },
  ])('成功响应$label时按 unavailable 处理', async ({ json }) => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({ ok: true, json } as Response)

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(refreshProtocolMocks.settleSessionRefreshSuccess).not.toHaveBeenCalled()
    expect(refreshProtocolMocks.settleSessionRefreshUnavailable).toHaveBeenCalledOnce()
  })

  it('刷新完成时 key 已过期返回 missing，不用 PX 复活', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'tok-new', expires_in: 3_600 }),
    } as Response)
    refreshProtocolMocks.settleSessionRefreshSuccess.mockResolvedValue('missing')

    await expect(getValidSessionResult()).resolves.toEqual({ status: 'missing' })
  })

  it('刷新令牌写回失败返回 unavailable', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'tok-new', expires_in: 3_600 }),
    } as Response)
    refreshProtocolMocks.settleSessionRefreshSuccess.mockResolvedValue('unavailable')

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
  })

  it('owner lease 已丢失时合法新 token 也不得迟到写回或删除当前会话', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(expiredSession()))
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'tok-late', expires_in: 3_600 }),
    } as Response)
    refreshProtocolMocks.settleSessionRefreshSuccess.mockResolvedValue('lost')

    await expect(getValidSessionResult()).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(invalidateStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    const event = vi.mocked(console.error).mock.calls.find(
      call => call[0] === '[session-refresh-event]',
    )
    expect(event?.[1]).toContain('"event":"oauth_success_settlement_degraded"')
    expect(event?.[1]).toContain('"settlement":"lost"')
    expect(event?.[1]).not.toContain('tok-late')
    expect(event?.[1]).not.toContain('refresh-old')
  })
})

describe('后端 401 的精确版本失效', () => {
  it('CAS 命中时只删除实际发起请求的会话 revision', async () => {
    setCookie('sid-1')

    await expect(
      invalidateSessionIfUnchanged(baseSession),
    ).resolves.toEqual({ status: 'invalidated' })

    expect(invalidateStoredSessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(baseSession),
    )
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(getStoredSessionMock).not.toHaveBeenCalled()
  })

  it('同身份 token A 到 A2 的 CAS 冲突返回 session-updated', async () => {
    setCookie('sid-1')
    const refreshed = {
      ...baseSession,
      accessToken: 'tok-a2',
      refreshToken: 'refresh-a2',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('conflict')
    getStoredSessionMock.mockResolvedValue(found(refreshed))

    await expect(
      invalidateSessionIfUnchanged(baseSession),
    ).resolves.toEqual({
      status: 'session-updated',
      session: refreshed,
    })
    expect(invalidateStoredSessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(baseSession),
    )
  })

  it('跨身份 CAS 冲突返回 identity-changed，绝不删除并发胜者', async () => {
    setCookie('sid-1')
    const switched = {
      ...baseSession,
      identityVersion: 'identity-target-v2',
      userName: 'target-user',
      accessToken: 'target-token',
      refreshToken: undefined,
    }
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('conflict')
    getStoredSessionMock.mockResolvedValue(found(switched))

    await expect(
      invalidateSessionIfUnchanged(baseSession),
    ).resolves.toEqual({
      status: 'identity-changed',
      session: switched,
    })
  })

  it('CAS 存储不可用时返回 unavailable，不伪装为已失效', async () => {
    setCookie('sid-1')
    invalidateStoredSessionIfUnchangedMock.mockRejectedValue(new Error('redis down'))

    await expect(
      invalidateSessionIfUnchanged(baseSession),
    ).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(getStoredSessionMock).not.toHaveBeenCalled()
  })

  it('非 UUID swapmid_sid 在任何存储访问前按 missing 隔离', async () => {
    setCookie('sid-1:refresh:v1')
    isValidSessionIdMock.mockReturnValue(false)

    await expect(getSessionResult()).resolves.toEqual({ status: 'missing' })
    await expect(getValidSessionResult()).resolves.toEqual({ status: 'missing' })
    expect(getStoredSessionMock).not.toHaveBeenCalled()
  })

  it('迟到 401 遇到同 source 活跃刷新时等待 owner，绝不普通 CAS 删除', async () => {
    setCookie('sid-1')
    const refreshed = {
      ...baseSession,
      accessToken: 'tok-refreshed',
      refreshToken: 'refresh-refreshed',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('refreshing')
    getRefreshLockMock.mockReturnValue(Promise.resolve(found(refreshed)))

    await expect(
      invalidateSessionIfUnchanged(baseSession),
    ).resolves.toEqual({
      status: 'session-updated',
      session: refreshed,
    })
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('未来一小时 token 遇到远端 owner 也必须强制 join，不能走 expiresAt 快路径返回旧 token', async () => {
    setCookie('sid-1')
    const future = {
      ...baseSession,
      accessToken: 'tok-r0',
      refreshToken: 'refresh-r0',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const winner = {
      ...future,
      accessToken: 'tok-r1',
      refreshToken: 'refresh-r1',
      accessTokenExpiresAt: Date.now() + 7_200_000,
    }
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('refreshing')
    refreshProtocolMocks.acquireSessionRefreshLease
      .mockResolvedValueOnce({ status: 'waiter', retryAfterMs: 1 })
      .mockResolvedValueOnce({ status: 'changed' })
    getStoredSessionMock.mockResolvedValue(found(winner))

    await expect(
      invalidateSessionIfUnchanged(future),
    ).resolves.toEqual({
      status: 'session-updated',
      session: winner,
    })
    expect(fetchMock).not.toHaveBeenCalled()
    expect(refreshProtocolMocks.acquireSessionRefreshLease).toHaveBeenCalled()
  })

  it('普通本地刷新刚写入 cooldown 后，迟到 401 会在原预算内升级为 forced takeover', async () => {
    setCookie('sid-1')
    const future = {
      ...baseSession,
      accessToken: 'tok-r0',
      refreshToken: 'refresh-r0',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const localUnavailable = Promise.resolve({
      status: 'unavailable' as const,
      retryAfterSeconds: 2,
    })
    getRefreshLockMock
      .mockReturnValueOnce(localUnavailable)
      .mockReturnValue(undefined)
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('refreshing')
    refreshProtocolMocks.acquireSessionRefreshLease
      .mockResolvedValueOnce({ status: 'cooldown', retryAfterMs: 1 })
      .mockResolvedValueOnce({
        status: 'owner',
        lease: { ownerId: 'owner-2', fence: 2, source: 'source-r0' },
      })
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        access_token: 'tok-r1',
        refresh_token: 'refresh-r1',
        expires_in: 3_600,
      }),
    } as Response)

    await expect(
      invalidateSessionIfUnchanged(future),
    ).resolves.toEqual({
      status: 'session-updated',
      session: expect.objectContaining({
        accessToken: 'tok-r1',
        refreshToken: 'refresh-r1',
      }),
    })
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', localUnavailable)
    expect(refreshProtocolMocks.acquireSessionRefreshLease).toHaveBeenCalledTimes(2)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('本地 L1 阻塞 9.9 秒后不能在剩余 0.1 秒临界 takeover 并再启动 OAuth', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-29T00:00:00.000Z'))
    setCookie('sid-1')
    let resolveLocal!: (value: {
      status: 'unavailable'
      retryAfterSeconds: number
    }) => void
    const localPending = new Promise<{
      status: 'unavailable'
      retryAfterSeconds: number
    }>((resolve) => {
      resolveLocal = resolve
    })
    getRefreshLockMock
      .mockReturnValueOnce(localPending)
      .mockReturnValue(undefined)
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('refreshing')
    refreshProtocolMocks.acquireSessionRefreshLease.mockResolvedValue({
      status: 'budget-exhausted',
    })

    const resultPromise = invalidateSessionIfUnchanged(baseSession)
    await vi.advanceTimersByTimeAsync(9_900)
    resolveLocal({ status: 'unavailable', retryAfterSeconds: 2 })
    await vi.advanceTimersByTimeAsync(0)

    await expect(resultPromise).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(refreshProtocolMocks.acquireSessionRefreshLease).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(baseSession),
      expect.any(String),
      false,
    )
    expect(fetchMock).not.toHaveBeenCalled()
    expect(refreshProtocolMocks.settleSessionRefreshUnavailable).not.toHaveBeenCalled()
  })

  it('forced owner 缩短 OAuth deadline，并在总预算到点时让 fenced settle 后台安全完成', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-29T00:00:00.000Z'))
    setCookie('sid-1')
    let resolveLocal!: (value: {
      status: 'unavailable'
      retryAfterSeconds: number
    }) => void
    let resolveSettlement!: (value: 'updated') => void
    const localPending = new Promise<{
      status: 'unavailable'
      retryAfterSeconds: number
    }>((resolve) => {
      resolveLocal = resolve
    })
    getRefreshLockMock
      .mockReturnValueOnce(localPending)
      .mockReturnValue(undefined)
    invalidateStoredSessionIfUnchangedMock.mockResolvedValue('refreshing')
    refreshProtocolMocks.acquireSessionRefreshLease.mockResolvedValue({
      status: 'owner',
      lease: { ownerId: 'owner-deadline', fence: 3, source: 'source-r0' },
    })
    refreshProtocolMocks.settleSessionRefreshUnavailable.mockReturnValue(
      new Promise<'updated'>((resolve) => {
        resolveSettlement = resolve
      }),
    )
    fetchMock.mockImplementation((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        const signal = init.signal
        if (!signal) throw new Error('refresh request must have an AbortSignal')
        signal.addEventListener('abort', () => reject(signal.reason), { once: true })
      }))

    const resultPromise = invalidateSessionIfUnchanged(baseSession)
    await vi.advanceTimersByTimeAsync(2_000)
    resolveLocal({ status: 'unavailable', retryAfterSeconds: 2 })
    await vi.advanceTimersByTimeAsync(8_000)

    await expect(resultPromise).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(refreshProtocolMocks.settleSessionRefreshUnavailable).toHaveBeenCalledOnce()
    resolveSettlement('updated')
    await vi.advanceTimersByTimeAsync(0)
  })
})

describe('退出与模拟登录边界', () => {
  const target = {
    userId: 'user-target-id',
    userName: 'target-user',
    name: '目标用户',
    grantedPolicies: { 'SwapMid.Bar': false },
    accessToken: 'target-tok',
    accessTokenExpiresAt: Date.now() + 3_600_000,
  }

  it('Redis 不可用时保留服务端会话与 stale cookie，返回可重试 unavailable', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })

    await expect(
      deleteSession(baseSession.identityVersion!),
    ).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    expect(deleteStoredSessionIfUnchangedMock).not.toHaveBeenCalled()
    expect(cookieDeleteMock).not.toHaveBeenCalled()
  })

  it('请求本就没有 sid 时幂等成功且不写 Set-Cookie', async () => {
    setCookie(undefined)

    await expect(
      deleteSession(baseSession.identityVersion!),
    ).resolves.toEqual({ status: 'deleted' })
    expect(cookieDeleteMock).not.toHaveBeenCalled()
  })

  it('携带身份版本退出时仅 CAS 删除读取到的会话版本', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(baseSession))

    await expect(
      deleteSession(baseSession.identityVersion!),
    ).resolves.toEqual({ status: 'deleted' })

    expect(deleteStoredSessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(baseSession),
    )
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(cookieDeleteMock).not.toHaveBeenCalled()
  })

  it('第二次退出 CAS 冲突期间切到新身份时不误删新身份 cookie', async () => {
    setCookie('sid-1')
    const refreshed = {
      ...baseSession,
      accessToken: 'tok-refreshed',
    }
    const switched = {
      ...baseSession,
      identityVersion: 'identity-target-v2',
      userName: 'target-user',
      accessToken: 'target-token',
      refreshToken: undefined,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(baseSession))
      .mockResolvedValueOnce(found(refreshed))
      .mockResolvedValueOnce(found(switched))
    deleteStoredSessionIfUnchangedMock
      .mockResolvedValueOnce('conflict')
      .mockResolvedValueOnce('conflict')

    await expect(
      deleteSession(baseSession.identityVersion!),
    ).resolves.toEqual({
      status: 'identity-changed',
      identityVersion: switched.identityVersion,
    })

    expect(deleteStoredSessionIfUnchangedMock).toHaveBeenNthCalledWith(
      1,
      'sid-1',
      revisionOf(baseSession),
    )
    expect(deleteStoredSessionIfUnchangedMock).toHaveBeenNthCalledWith(
      2,
      'sid-1',
      revisionOf(refreshed),
    )
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(cookieDeleteMock).not.toHaveBeenCalled()
  })

  it('同一身份连续三次退出 CAS 冲突时保留 cookie 并要求调用方重试', async () => {
    setCookie('sid-1')
    const versions = ['tok-v1', 'tok-v2', 'tok-v3', 'tok-v4'].map(accessToken => ({
      ...baseSession,
      accessToken,
    }))
    getStoredSessionMock
      .mockResolvedValueOnce(found(versions[0]))
      .mockResolvedValueOnce(found(versions[1]))
      .mockResolvedValueOnce(found(versions[2]))
      .mockResolvedValueOnce(found(versions[3]))
    deleteStoredSessionIfUnchangedMock.mockResolvedValue('conflict')

    await expect(
      deleteSession(baseSession.identityVersion!),
    ).rejects.toThrow('SESSION_CONCURRENT_MUTATION')

    expect(deleteStoredSessionIfUnchangedMock).toHaveBeenCalledTimes(3)
    expect(deleteStoredSessionMock).not.toHaveBeenCalled()
    expect(cookieDeleteMock).not.toHaveBeenCalled()
  })

  it('开始模拟登录要求当前身份版本，保留原身份快照并旋转到新版本', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(baseSession))

    const next = await startImpersonation(target, baseSession.identityVersion!)

    expect(next).toEqual(expect.objectContaining({
      userId: 'user-target-id',
      userName: 'target-user',
      accessToken: 'target-tok',
      refreshToken: undefined,
      identityVersion: expect.any(String),
      impersonator: expect.objectContaining({
        userId: 'user-admin-id',
        userName: 'admin',
        refreshToken: 'refresh-old',
        identityVersion: baseSession.identityVersion,
      }),
    }))
    expect(next.identityVersion).not.toBe(baseSession.identityVersion)
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(baseSession),
      next,
    )
  })

  it('开始模拟遇到远端刷新时强制 join R1，并以 R1 重建 impersonator 后有限重试', async () => {
    setCookie('sid-1')
    const r0 = {
      ...baseSession,
      accessToken: 'tok-r0',
      refreshToken: 'refresh-r0',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const r1 = {
      ...r0,
      accessToken: 'tok-r1',
      refreshToken: 'refresh-r1',
      accessTokenExpiresAt: Date.now() + 7_200_000,
    }
    getStoredSessionMock
      .mockResolvedValueOnce(found(r0))
      .mockResolvedValueOnce(found(r1))
    replaceIdentitySessionIfUnchangedMock
      .mockResolvedValueOnce('refreshing')
      .mockResolvedValueOnce('updated')
    refreshProtocolMocks.acquireSessionRefreshLease
      .mockResolvedValueOnce({ status: 'waiter', retryAfterMs: 1 })
      .mockResolvedValueOnce({ status: 'changed' })

    const next = await startImpersonation(target, r0.identityVersion!)

    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledTimes(2)
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenNthCalledWith(
      2,
      'sid-1',
      revisionOf(r1),
      next,
    )
    expect(next.impersonator).toEqual(expect.objectContaining({
      accessToken: 'tok-r1',
      refreshToken: 'refresh-r1',
      accessTokenExpiresAt: r1.accessTokenExpiresAt,
    }))
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('开始模拟遇到本地普通刷新 unavailable 时继续穿过 cooldown，并用 takeover 的 R1 建快照', async () => {
    setCookie('sid-1')
    const r0 = {
      ...baseSession,
      accessToken: 'tok-r0',
      refreshToken: 'refresh-r0',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const localUnavailable = Promise.resolve({
      status: 'unavailable' as const,
      retryAfterSeconds: 2,
    })
    getStoredSessionMock.mockResolvedValue(found(r0))
    getRefreshLockMock
      .mockReturnValueOnce(localUnavailable)
      .mockReturnValue(undefined)
    replaceIdentitySessionIfUnchangedMock
      .mockResolvedValueOnce('refreshing')
      .mockResolvedValueOnce('updated')
    refreshProtocolMocks.acquireSessionRefreshLease
      .mockResolvedValueOnce({ status: 'cooldown', retryAfterMs: 1 })
      .mockResolvedValueOnce({
        status: 'owner',
        lease: { ownerId: 'owner-2', fence: 2, source: 'source-r0' },
      })
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        access_token: 'tok-r1',
        refresh_token: 'refresh-r1',
        expires_in: 3_600,
      }),
    } as Response)

    const next = await startImpersonation(target, r0.identityVersion!)

    expect(next.impersonator).toEqual(expect.objectContaining({
      accessToken: 'tok-r1',
      refreshToken: 'refresh-r1',
    }))
    expect(clearRefreshLockMock).toHaveBeenCalledWith('sid-1', localUnavailable)
    expect(refreshProtocolMocks.acquireSessionRefreshLease).toHaveBeenCalledTimes(2)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('开始模拟登录时没有 cookie 或存储已失效均返回 SESSION_MISSING', async () => {
    setCookie(undefined)
    await expect(
      startImpersonation(target, baseSession.identityVersion!),
    ).rejects.toThrow('SESSION_MISSING')

    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({ status: 'missing' })
    await expect(
      startImpersonation(target, baseSession.identityVersion!),
    ).rejects.toThrow('SESSION_MISSING')
  })

  it('当前已在模拟登录时拒绝链式切换', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found({
      ...baseSession,
      impersonator: {
        ...baseSession,
        accessTokenExpiresAt: Date.now() + 3_600_000,
      },
    }))

    await expect(
      startImpersonation(target, baseSession.identityVersion!),
    ).rejects.toThrow('IMPERSONATION_ALREADY_ACTIVE')
    expect(replaceIdentitySessionIfUnchangedMock).not.toHaveBeenCalled()
  })

  it('开始模拟登录时会话存储不可用明确失败，不伪装成未登录', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })

    await expect(
      startImpersonation(target, baseSession.identityVersion!),
    ).rejects.toThrow('SESSION_UNAVAILABLE')
  })

  it('开始模拟登录写回时 key 已消失明确失败', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(baseSession))
    replaceIdentitySessionIfUnchangedMock.mockResolvedValue('missing')

    await expect(
      startImpersonation(target, baseSession.identityVersion!),
    ).rejects.toThrow('SESSION_MISSING')
  })

  it('开始或停止模拟登录时拒绝陈旧身份版本且不写回', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue(found(baseSession))

    await expect(
      startImpersonation(target, 'identity-stale-v0'),
    ).rejects.toThrow('SESSION_IDENTITY_CHANGED')
    await expect(
      stopImpersonation('identity-stale-v0'),
    ).rejects.toThrow('SESSION_IDENTITY_CHANGED')
    expect(replaceIdentitySessionIfUnchangedMock).not.toHaveBeenCalled()
  })

  it('切回原身份时清空模拟态并再次旋转身份版本', async () => {
    setCookie('sid-1')
    const impersonator = {
      ...baseSession,
      accessToken: 'admin-token',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const current: FullSession = {
      ...baseSession,
      userName: 'target',
      identityVersion: 'identity-target-v1',
      impersonator,
    }
    getStoredSessionMock.mockResolvedValue(found(current))

    const restored = await stopImpersonation(current.identityVersion!)

    expect(restored).toEqual(expect.objectContaining({
      userId: 'user-admin-id',
      userName: impersonator.userName,
      accessToken: impersonator.accessToken,
      identityVersion: expect.any(String),
    }))
    expect(restored?.identityVersion).not.toBe(current.identityVersion)
    expect(restored?.identityVersion).not.toBe(impersonator.identityVersion)
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledWith(
      'sid-1',
      revisionOf(current),
      restored,
    )
  })

  it('切回遇到 refreshing 且模拟态无 refreshToken 时只重读再 CAS，不 forced join', async () => {
    setCookie('sid-1')
    const impersonator = {
      ...baseSession,
      accessToken: 'admin-token',
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const current: FullSession = {
      ...baseSession,
      userName: 'target',
      identityVersion: 'identity-target-v1',
      refreshToken: undefined,
      impersonator,
    }
    getStoredSessionMock.mockResolvedValue(found(current))
    replaceIdentitySessionIfUnchangedMock
      .mockResolvedValueOnce('refreshing')
      .mockResolvedValueOnce('updated')

    const restored = await stopImpersonation(current.identityVersion!)

    expect(restored).toEqual(expect.objectContaining({
      userName: impersonator.userName,
      accessToken: impersonator.accessToken,
    }))
    expect(refreshProtocolMocks.acquireSessionRefreshLease).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()
    expect(replaceIdentitySessionIfUnchangedMock).toHaveBeenCalledTimes(2)
  })

  it('切回时会话已经消失返回 null，存储不可用则明确失败', async () => {
    setCookie('sid-1')
    getStoredSessionMock.mockResolvedValue({ status: 'missing' })
    await expect(stopImpersonation('identity-target-v1')).resolves.toBeNull()

    getStoredSessionMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
    await expect(
      stopImpersonation('identity-target-v1'),
    ).rejects.toThrow('SESSION_UNAVAILABLE')
  })
})
