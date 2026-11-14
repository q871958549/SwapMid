import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/lib/session', () => ({ getValidSessionResult: vi.fn() }))

import { getValidSessionResult } from '@/lib/session'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { GET } from './route'

const getValidSessionResultMock = vi.mocked(getValidSessionResult)
const SESSION = {
  identityVersion: 'identity-current',
  accessToken: 'AT-1',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  accessTokenExpiresAt: Date.now() + 3_600_000,
}

beforeEach(() => {
  vi.clearAllMocks()
  getValidSessionResultMock.mockResolvedValue({ status: 'found', session: SESSION })
})

describe('session identity version route', () => {
  it('返回当前权威身份版本且禁止缓存', async () => {
    const res = await GET()

    expect(res.status).toBe(200)
    expect(await res.json()).toEqual({ identityVersion: 'identity-current' })
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-current')
    expect(res.headers.get('cache-control')).toBe('private, no-store')
  })

  it('自动切回刚发生时仍返回切回后的权威版本', async () => {
    getValidSessionResultMock.mockResolvedValue({
      status: 'identity-changed',
      reason: 'impersonation-expired',
      session: { ...SESSION, identityVersion: 'identity-restored' },
    })

    const res = await GET()

    expect(res.status).toBe(200)
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-restored')
  })

  it('缺失会话返回 401；存储不可用返回 tagged 503', async () => {
    getValidSessionResultMock.mockResolvedValueOnce({ status: 'missing' })
    const missing = await GET()

    getValidSessionResultMock.mockResolvedValueOnce({
      status: 'unavailable',
      retryAfterSeconds: 4,
    })
    const unavailable = await GET()

    expect(missing.status).toBe(401)
    expect(missing.headers.get('cache-control')).toBe('private, no-store')
    expect(unavailable.status).toBe(503)
    expect(unavailable.headers.get(SESSION_STATE_HEADER)).toBe('unavailable')
    expect(unavailable.headers.get('retry-after')).toBe('4')
  })
})
