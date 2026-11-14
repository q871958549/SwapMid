import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FullSession } from './session-types'

const redisMock = vi.hoisted(() => ({
  get: vi.fn(),
  set: vi.fn(),
  del: vi.fn(),
  eval: vi.fn(),
  on: vi.fn(),
  constructor: vi.fn(),
}))

vi.mock('ioredis', () => ({
  Redis: class {
    constructor(url: string, options: Record<string, unknown>) {
      redisMock.constructor(url, options)
    }

    get(...args: unknown[]) {
      return redisMock.get(...args)
    }

    set(...args: unknown[]) {
      return redisMock.set(...args)
    }

    del(...args: unknown[]) {
      return redisMock.del(...args)
    }

    eval(...args: unknown[]) {
      return redisMock.eval(...args)
    }

    on(...args: unknown[]) {
      return redisMock.on(...args)
    }
  },
}))

vi.spyOn(console, 'error').mockImplementation(() => {})

const session: FullSession = {
  userId: 'user-admin-id',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: { Foo: true },
  identityVersion: 'identity-admin-v1',
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  accessTokenExpiresAt: Date.now() + 3_600_000,
}
const SID = '11111111-1111-4111-8111-111111111111'

function revisionOf(value: FullSession) {
  return {
    identityVersion: value.identityVersion ?? '',
    userName: value.userName,
    accessToken: value.accessToken,
    refreshToken: value.refreshToken ?? '',
    accessTokenExpiresAt: value.accessTokenExpiresAt,
  }
}

type SessionGlobals = typeof globalThis & {
  __swapmidSessionStore?: Map<string, unknown>
  __swapmidRefreshLocks?: Map<string, unknown>
  __swapmidRedis?: unknown
}

function resetSessionGlobals() {
  const globals = globalThis as SessionGlobals
  delete globals.__swapmidSessionStore
  delete globals.__swapmidRefreshLocks
  delete globals.__swapmidRedis
}

beforeEach(() => {
  vi.resetModules()
  vi.clearAllMocks()
  vi.useRealTimers()
  resetSessionGlobals()
  vi.stubEnv('REDIS_URL', 'redis://redis.test:6379/1')
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllEnvs()
  resetSessionGlobals()
})

describe('session-store · Redis 三态与超时', () => {
  // 本用例是 describe 内第一条：beforeEach 的 vi.resetModules() 之后，这次 import 要重编整个
  // 模块图，全量并发跑（153 文件）时会撞默认 5000ms 超时而假红；同文件后续用例吃缓存不受影响，
  // 故只此一条显式放宽。判定假红的三条判据见 memory web-testing「假红有两种形态」。
  it('Redis 客户端配置命令级超时，避免请求无限悬挂', async () => {
    redisMock.get.mockResolvedValue(null)
    const { getStoredSession } = await import('./session-store')

    await getStoredSession(SID)

    expect(redisMock.constructor).toHaveBeenCalledWith(
      'redis://redis.test:6379/1',
      expect.objectContaining({
        maxRetriesPerRequest: 2,
        connectTimeout: 3_000,
        commandTimeout: 1_500,
        lazyConnect: false,
      }),
    )
  }, 15_000)

  it('新旧有效 JSON 均返回 found，空 key 返回 missing', async () => {
    const { getStoredSession } = await import('./session-store')
    const legacySession: FullSession = { ...session }
    delete legacySession.userId
    redisMock.get
      .mockResolvedValueOnce(JSON.stringify(session))
      .mockResolvedValueOnce(JSON.stringify(legacySession))
      .mockResolvedValueOnce(null)

    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'found',
      session,
    })
    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'found',
      session: legacySession,
    })
    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'missing',
    })
  })

  it('Redis GET 失败返回 unavailable，不伪装成 missing', async () => {
    const { getStoredSession } = await import('./session-store')
    redisMock.get.mockRejectedValue(new Error('connection reset'))

    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'unavailable',
      retryAfterSeconds: 2,
    })
  })

  it.each([
    { label: '语法损坏', corrupt: '{"accessToken":"secret-token"' },
    {
      label: '结构损坏',
      corrupt: JSON.stringify({
        ...session,
        accessToken: '',
        grantedPolicies: { Foo: 'yes' },
      }),
    },
    {
      label: '空账号主键',
      corrupt: JSON.stringify({
        ...session,
        userId: ' ',
      }),
    },
    {
      label: '超长账号主键',
      corrupt: JSON.stringify({
        ...session,
        userId: 'x'.repeat(257),
      }),
    },
    {
      label: '模拟者快照账号主键损坏',
      corrupt: JSON.stringify({
        ...session,
        impersonator: {
          ...session,
          userId: '',
        },
      }),
    },
  ])('单条$label会话按 missing 隔离并尝试清理，不泄露令牌原文', async ({ corrupt }) => {
    const { getStoredSession } = await import('./session-store')
    redisMock.get.mockResolvedValue(corrupt)
    redisMock.del.mockResolvedValue(1)

    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'missing',
    })
    expect(redisMock.del).toHaveBeenCalledWith(
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
    )
    expect(console.error).not.toHaveBeenCalledWith(expect.stringContaining('secret-token'))
  })
})

describe('session-store · TTL 与刷新/登出竞态', () => {
  it('新建会话使用 8 小时 TTL', async () => {
    const { storeSession } = await import('./session-store')
    redisMock.set.mockResolvedValue('OK')

    await storeSession(SID, session)

    expect(redisMock.set).toHaveBeenCalledWith(
      `swapmid:sess:${SID}`,
      JSON.stringify(session),
      'PX',
      8 * 60 * 60 * 1_000,
    )
  })

  it('Redis 条件替换通过 Lua 原子校验修订版并映射三态结果', async () => {
    const { replaceStoredSessionIfUnchanged } = await import('./session-store')
    const updated = {
      ...session,
      accessToken: 'new-access-token',
    }
    redisMock.eval
      .mockResolvedValueOnce(1)
      .mockResolvedValueOnce(0)
      .mockResolvedValueOnce(-1)

    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), updated),
    ).resolves.toBe('updated')
    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), updated),
    ).resolves.toBe('missing')
    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), updated),
    ).resolves.toBe('conflict')

    expect(redisMock.eval).toHaveBeenNthCalledWith(
      1,
      expect.stringContaining("'KEEPTTL', 'XX'"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      session.identityVersion,
      session.userName,
      session.accessToken,
      session.refreshToken,
      session.accessTokenExpiresAt,
      'replace',
      JSON.stringify(updated),
    )
  })

  it('Redis 条件删除复用同一原子 CAS 且不携带替换载荷', async () => {
    const { deleteStoredSessionIfUnchanged } = await import('./session-store')
    redisMock.eval.mockResolvedValue(1)

    await expect(
      deleteStoredSessionIfUnchanged(SID, revisionOf(session)),
    ).resolves.toBe('updated')

    expect(redisMock.eval).toHaveBeenCalledWith(
      expect.stringContaining("ARGV[6] == 'delete'"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      session.identityVersion,
      session.userName,
      session.accessToken,
      session.refreshToken,
      session.accessTokenExpiresAt,
      'delete',
      '',
    )
  })

  it('迟到 401 遇到同 source lease 时返回 refreshing，不执行普通删除', async () => {
    const { invalidateStoredSessionIfUnchanged } = await import('./session-store')
    redisMock.eval.mockResolvedValue(2)

    await expect(
      invalidateStoredSessionIfUnchanged(SID, revisionOf(session)),
    ).resolves.toBe('refreshing')

    expect(redisMock.eval).toHaveBeenCalledWith(
      expect.stringContaining("lease['source'] == ARGV[6]"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      session.identityVersion,
      session.userName,
      session.accessToken,
      session.refreshToken,
      session.accessTokenExpiresAt,
      expect.stringMatching(/^[A-Za-z0-9_-]{43}$/),
    )
  })

  it('身份切换遇到同 source active/cooldown lease 时返回 refreshing 且不清 lease', async () => {
    const { replaceIdentitySessionIfUnchanged } = await import('./session-store')
    redisMock.eval.mockResolvedValue(2)
    const target = {
      ...session,
      identityVersion: 'identity-target-v2',
      userName: 'target',
      accessToken: 'target-token',
      refreshToken: undefined,
    }

    await expect(
      replaceIdentitySessionIfUnchanged(SID, revisionOf(session), target),
    ).resolves.toBe('refreshing')

    expect(redisMock.eval).toHaveBeenCalledWith(
      expect.stringContaining("lease['state'] == 'active'"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      session.identityVersion,
      session.userName,
      session.accessToken,
      session.refreshToken,
      session.accessTokenExpiresAt,
      expect.stringMatching(/^[A-Za-z0-9_-]{43}$/),
      JSON.stringify(target),
    )
    const script = String(redisMock.eval.mock.calls[0][0])
    expect(script).toContain("lease['state'] == 'cooldown'")
    expect(script).toContain("redis.call('PTTL', KEYS[2]) > 0")
    expect(script.indexOf('return 2')).toBeLessThan(
      script.indexOf("'KEEPTTL', 'XX'"),
    )
  })

  it('删除会话只删除精确命名空间 key', async () => {
    const {
      deleteStoredSession,
      getRefreshLock,
      setRefreshLock,
    } = await import('./session-store')
    redisMock.del.mockResolvedValue(1)
    setRefreshLock(SID, Promise.resolve({ status: 'found', session }))

    await deleteStoredSession(SID)

    expect(redisMock.del).toHaveBeenCalledWith(
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
    )
    expect(getRefreshLock(SID)).toBeUndefined()
  })

  it('内存开发后端执行同等 CAS，冲突不会覆盖新会话', async () => {
    vi.stubEnv('REDIS_URL', '')
    const {
      deleteStoredSessionIfUnchanged,
      getStoredSession,
      replaceStoredSessionIfUnchanged,
      storeSession,
    } = await import('./session-store')
    const updated = {
      ...session,
      identityVersion: 'identity-admin-v2',
      accessToken: 'new-token',
    }

    await storeSession(SID, session)
    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), updated),
    ).resolves.toBe('updated')
    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), {
        ...session,
        accessToken: 'stale-writer-token',
      }),
    ).resolves.toBe('conflict')
    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'found',
      session: updated,
    })
    await expect(
      deleteStoredSessionIfUnchanged(SID, revisionOf(updated)),
    ).resolves.toBe('updated')
    await expect(getStoredSession(SID)).resolves.toEqual({ status: 'missing' })
  })

  it('内存后端刷新成功时按传入 TTL 滑动续期', async () => {
    vi.stubEnv('REDIS_URL', '')
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-27T00:00:00Z'))
    const {
      getMemorySessionRemainingTtl,
      replaceStoredSessionIfUnchanged,
      storeSession,
    } = await import('./session-store')

    await storeSession(SID, session)
    vi.advanceTimersByTime(60_000)
    const updated = {
      ...session,
      accessToken: 'slid-token',
    }

    await expect(
      replaceStoredSessionIfUnchanged(
        SID,
        revisionOf(session),
        updated,
        8 * 60 * 60 * 1_000,
      ),
    ).resolves.toBe('updated')
    expect(getMemorySessionRemainingTtl(SID)).toBe(8 * 60 * 60 * 1_000)
  })

  it('内存开发后端不会条件更新已过期会话或把它复活', async () => {
    vi.stubEnv('REDIS_URL', '')
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-27T00:00:00Z'))
    const {
      getStoredSession,
      replaceStoredSessionIfUnchanged,
      storeSession,
    } = await import('./session-store')

    await storeSession(SID, session)
    vi.advanceTimersByTime(8 * 60 * 60 * 1_000 + 1)

    await expect(
      replaceStoredSessionIfUnchanged(SID, revisionOf(session), {
        ...session,
        accessToken: 'new-token',
      }),
    ).resolves.toBe('missing')
    await expect(getStoredSession(SID)).resolves.toEqual({ status: 'missing' })
  })

  it('内存身份切换在本地刷新活跃时返回 refreshing，胜者完成后才能落新身份', async () => {
    vi.stubEnv('REDIS_URL', '')
    const {
      clearRefreshLock,
      getStoredSession,
      replaceIdentitySessionIfUnchanged,
      setRefreshLock,
      storeSession,
    } = await import('./session-store')
    const refreshPromise = Promise.resolve({ status: 'found' as const, session })
    const target = {
      ...session,
      identityVersion: 'identity-target-v2',
      userName: 'target',
      accessToken: 'target-token',
      refreshToken: undefined,
    }
    await storeSession(SID, session)
    setRefreshLock(SID, refreshPromise)

    await expect(
      replaceIdentitySessionIfUnchanged(SID, revisionOf(session), target),
    ).resolves.toBe('refreshing')
    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'found',
      session,
    })

    expect(clearRefreshLock(SID, refreshPromise)).toBe(true)
    await expect(
      replaceIdentitySessionIfUnchanged(SID, revisionOf(session), target),
    ).resolves.toBe('updated')
    await expect(getStoredSession(SID)).resolves.toEqual({
      status: 'found',
      session: target,
    })
  })

  it('旧 Promise finally 只能 compare-delete 自己，不能删除 mutation 后建立的新 Promise', async () => {
    const {
      clearRefreshLock,
      getRefreshLock,
      replaceStoredSessionIfUnchanged,
      setRefreshLock,
    } = await import('./session-store')
    redisMock.eval.mockResolvedValue(1)
    const first = Promise.resolve({ status: 'found' as const, session })
    const second = Promise.resolve({ status: 'found' as const, session })
    setRefreshLock(SID, first)

    await replaceStoredSessionIfUnchanged(
      SID,
      revisionOf(session),
      { ...session, accessToken: 'winner-token' },
    )
    setRefreshLock(SID, second)

    expect(clearRefreshLock(SID, first)).toBe(false)
    expect(getRefreshLock(SID)).toBe(second)
    expect(clearRefreshLock(SID, second)).toBe(true)
  })

  it.each(['replace', 'invalidate', 'identity'] as const)(
    '%s mutation 等待 Redis 返回期间不能清掉后来建立的 L1 Promise',
    async (operation) => {
      const {
        getRefreshLock,
        invalidateStoredSessionIfUnchanged,
        replaceIdentitySessionIfUnchanged,
        replaceStoredSessionIfUnchanged,
        setRefreshLock,
      } = await import('./session-store')
      let settleEval!: (value: number) => void
      redisMock.eval.mockReturnValueOnce(new Promise<number>((resolve) => {
        settleEval = resolve
      }))
      const observed = Promise.resolve({ status: 'found' as const, session })
      const newer = Promise.resolve({ status: 'found' as const, session })
      const target = {
        ...session,
        identityVersion: 'identity-target-v2',
        userName: 'target',
        accessToken: 'target-token',
        refreshToken: undefined,
      }
      setRefreshLock(SID, observed)

      const mutation = operation === 'replace'
        ? replaceStoredSessionIfUnchanged(SID, revisionOf(session), target)
        : operation === 'invalidate'
          ? invalidateStoredSessionIfUnchanged(SID, revisionOf(session))
          : replaceIdentitySessionIfUnchanged(SID, revisionOf(session), target)
      setRefreshLock(SID, newer)
      settleEval(1)

      await expect(mutation).resolves.toBe('updated')
      expect(getRefreshLock(SID)).toBe(newer)
    },
  )

  it('非法 sid 在触达 Redis 前按 missing 隔离，不能碰撞刷新控制 key', async () => {
    const {
      getStoredSession,
      isValidSessionId,
      replaceStoredSessionIfUnchanged,
    } = await import('./session-store')

    expect(isValidSessionId(SID)).toBe(true)
    expect(isValidSessionId(`${SID}:refresh:v1`)).toBe(false)
    await expect(getStoredSession(`${SID}:refresh:v1`)).resolves.toEqual({
      status: 'missing',
    })
    await expect(
      replaceStoredSessionIfUnchanged('sid-1', revisionOf(session), session),
    ).resolves.toBe('missing')
    expect(redisMock.get).not.toHaveBeenCalled()
    expect(redisMock.eval).not.toHaveBeenCalled()
  })

  it('精确 source 摘要覆盖 refreshToken 与到期时间且不含原 token', async () => {
    const { sessionRevisionDigest } = await import('./session-store')
    const digest = sessionRevisionDigest(revisionOf(session))
    const rotated = sessionRevisionDigest(revisionOf({
      ...session,
      refreshToken: 'refresh-token-2',
    }))
    const renewed = sessionRevisionDigest(revisionOf({
      ...session,
      accessTokenExpiresAt: session.accessTokenExpiresAt + 1,
    }))

    expect(digest).toMatch(/^[A-Za-z0-9_-]{43}$/)
    expect(digest).not.toContain(session.accessToken)
    expect(digest).not.toContain(session.refreshToken!)
    expect(rotated).not.toBe(digest)
    expect(renewed).not.toBe(digest)
  })
})
