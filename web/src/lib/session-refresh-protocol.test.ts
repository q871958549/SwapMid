import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { FullSession } from './session-types'

const redisMock = vi.hoisted(() => ({
  eval: vi.fn(),
}))

vi.mock('./session-store', () => ({
  deleteStoredSession: vi.fn(),
  deleteStoredSessionIfUnchanged: vi.fn(),
  getMemorySessionRemainingTtl: vi.fn(),
  getSessionStoreRedis: vi.fn(() => redisMock),
  getStoredSession: vi.fn(),
  isValidSessionId: vi.fn(() => true),
  matchesRevision: vi.fn(),
  refreshFenceKey: (id: string) => `swapmid:sess:${id}:refresh:fence:v1`,
  refreshLeaseKey: (id: string) => `swapmid:sess:${id}:refresh:v1`,
  replaceStoredSessionIfUnchanged: vi.fn(),
  sessionDataKey: (id: string) => `swapmid:sess:${id}`,
  sessionRevisionDigest: vi.fn(() => 'source-digest'),
}))

import {
  acquireSessionRefreshLease,
  settleSessionRefreshInvalidGrant,
  settleSessionRefreshSuccess,
  settleSessionRefreshUnavailable,
} from './session-refresh-protocol'
import type { SessionRevision } from './session-store'

const SID = '11111111-1111-4111-8111-111111111111'
const revision: SessionRevision = {
  identityVersion: 'identity-v1',
  userName: 'admin',
  accessToken: 'access-secret',
  refreshToken: 'refresh-secret',
  accessTokenExpiresAt: 1_800_000_000_000,
}
const lease = {
  ownerId: 'owner-1',
  fence: 7,
  source: 'source-digest',
}
const updated: FullSession = {
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  identityVersion: 'identity-v1',
  accessToken: 'access-new',
  refreshToken: 'refresh-new',
  accessTokenExpiresAt: revision.accessTokenExpiresAt + 3_600_000,
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('Redis refresh owner/fence/source 协议', () => {
  it('原子争主映射 owner/waiter/cooldown/changed/missing', async () => {
    redisMock.eval
      .mockResolvedValueOnce([1, 7, 15_000])
      .mockResolvedValueOnce([2, 7, 14_000])
      .mockResolvedValueOnce([4, 1_500])
      .mockResolvedValueOnce([3])
      .mockResolvedValueOnce([0])
      .mockResolvedValueOnce([5])

    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-1'),
    ).resolves.toEqual({ status: 'owner', lease })
    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-2'),
    ).resolves.toEqual({ status: 'waiter', retryAfterMs: 14_000 })
    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-3'),
    ).resolves.toEqual({ status: 'cooldown', retryAfterMs: 1_500 })
    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-4'),
    ).resolves.toEqual({ status: 'changed' })
    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-5'),
    ).resolves.toEqual({ status: 'missing' })
    await expect(
      acquireSessionRefreshLease(SID, revision, 'owner-6', false),
    ).resolves.toEqual({ status: 'budget-exhausted' })

    expect(redisMock.eval).toHaveBeenNthCalledWith(
      1,
      expect.stringContaining("redis.call('INCR', KEYS[3])"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      revision.identityVersion,
      revision.userName,
      revision.accessToken,
      revision.refreshToken,
      revision.accessTokenExpiresAt,
      'source-digest',
      'owner-1',
      15_000,
      12_000,
      '1',
    )
    expect(redisMock.eval.mock.calls[5].at(-1)).toBe('0')
    const script = String(redisMock.eval.mock.calls[0][0])
    expect(script).toContain("source = ARGV[6]")
    expect(script).toContain('leaseTtl > 0')
    expect(script).toContain("ARGV[10] ~= '1'")
    expect(script).not.toContain("refreshToken = ARGV[4]")
    expect(script).not.toContain("accessToken = ARGV[3]")
  })

  it('超过绝对上限时 success 结算传入 slideTtl=0，交给 Lua 删除', async () => {
    redisMock.eval.mockResolvedValue(0)
    const expired: FullSession = {
      ...updated,
      issuedAt: Date.now() - 7 * 24 * 60 * 60 * 1_000 - 1,
    }

    await expect(
      settleSessionRefreshSuccess(SID, revision, lease, expired),
    ).resolves.toBe('missing')

    expect(redisMock.eval.mock.calls[0].at(-1)).toBe(0)
  })

  it('success 结算在同一 Lua 校验 owner/fence/source 并滑动 PX 写回', async () => {
    redisMock.eval.mockResolvedValue(1)

    await expect(
      settleSessionRefreshSuccess(SID, revision, lease, updated),
    ).resolves.toBe('updated')

    expect(redisMock.eval).toHaveBeenCalledWith(
      expect.stringContaining("'PX', slideTtl, 'XX'"),
      3,
      `swapmid:sess:${SID}`,
      `swapmid:sess:${SID}:refresh:v1`,
      `swapmid:sess:${SID}:refresh:fence:v1`,
      lease.ownerId,
      lease.fence,
      lease.source,
      revision.identityVersion,
      revision.userName,
      revision.accessToken,
      revision.refreshToken,
      revision.accessTokenExpiresAt,
      'success',
      JSON.stringify(updated),
      2_000,
      8 * 60 * 60 * 1_000,
    )
  })

  it('invalid_grant 只能由当前 owner/fence 删除，迟到 owner 返回 lost', async () => {
    redisMock.eval
      .mockResolvedValueOnce(1)
      .mockResolvedValueOnce(-2)

    await expect(
      settleSessionRefreshInvalidGrant(SID, revision, lease),
    ).resolves.toBe('updated')
    await expect(
      settleSessionRefreshInvalidGrant(SID, revision, {
        ...lease,
        fence: lease.fence - 1,
      }),
    ).resolves.toBe('lost')

    expect(String(redisMock.eval.mock.calls[0][0])).toContain(
      "ARGV[9] == 'invalid'",
    )
  })

  it('非终态失败写短 cooldown；Redis 异常明确 unavailable', async () => {
    redisMock.eval
      .mockResolvedValueOnce(1)
      .mockRejectedValueOnce(new Error('redis down'))

    await expect(
      settleSessionRefreshUnavailable(SID, revision, lease),
    ).resolves.toBe('updated')
    await expect(
      settleSessionRefreshUnavailable(SID, revision, lease),
    ).resolves.toBe('unavailable')
    expect(String(redisMock.eval.mock.calls[0][0])).toContain(
      "state = 'cooldown'",
    )
  })
})
