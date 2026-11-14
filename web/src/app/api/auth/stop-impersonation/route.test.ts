import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'

const mocks = vi.hoisted(() => ({
  stopImpersonation: vi.fn(),
  getSessionIdentityVersion: vi.fn(
    (session: { identityVersion?: string }) => session.identityVersion ?? 'legacy',
  ),
}))

vi.mock('@/lib/session', () => ({
  stopImpersonation: mocks.stopImpersonation,
  SessionIdentityChangedError: class SessionIdentityChangedError extends Error {
    constructor(readonly identityVersion: string) {
      super('SESSION_IDENTITY_CHANGED')
      this.name = 'SessionIdentityChangedError'
    }
  },
}))
vi.mock('@/lib/session-identity', () => ({
  getSessionIdentityVersion: mocks.getSessionIdentityVersion,
}))

vi.spyOn(console, 'error').mockImplementation(() => {})

import { SessionIdentityChangedError, stopImpersonation } from '@/lib/session'
import {
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { POST } from './route'

const stopImpersonationMock = vi.mocked(stopImpersonation)

function makeReq(identityVersion?: string): NextRequest {
  return {
    headers: new Headers(
      identityVersion
        ? { [SESSION_IDENTITY_VERSION_HEADER]: identityVersion }
        : undefined,
    ),
  } as unknown as NextRequest
}

beforeEach(() => {
  vi.clearAllMocks()
  stopImpersonationMock.mockResolvedValue({
    identityVersion: 'identity-admin-v2',
    userName: 'admin',
    name: '管理员',
    grantedPolicies: {},
    accessToken: 'admin-token',
    accessTokenExpiresAt: Date.now() + 60_000,
  })
})

describe('stop-impersonation route', () => {
  it('按当前文档身份世代切回，并回写新世代', async () => {
    const response = await POST(makeReq('identity-target-v1'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({
      ok: true,
      identityVersion: 'identity-admin-v2',
    })
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-admin-v2')
    expect(stopImpersonationMock).toHaveBeenCalledWith('identity-target-v1')
  })

  it('身份已变时返回 200 identityChanged，不发 409 以免 Guard 停在业务页刷新', async () => {
    stopImpersonationMock.mockRejectedValue(
      new SessionIdentityChangedError('identity-admin-v3'),
    )

    const response = await POST(makeReq('identity-stale-v0'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({
      identityChanged: true,
      identityVersion: 'identity-admin-v3',
    })
    expect(response.headers.get(SESSION_STATE_HEADER)).toBeNull()
    expect(response.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-admin-v3')
  })

  it('会话已消失返回 tagged 401', async () => {
    stopImpersonationMock.mockResolvedValue(null)

    const response = await POST(makeReq('identity-target-v1'))

    expect(response.status).toBe(401)
    expect(await response.json()).toEqual({ error: 'session-expired' })
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('missing')
  })

  it('存储不可用返回 tagged 503', async () => {
    stopImpersonationMock.mockRejectedValue(new Error('SESSION_UNAVAILABLE'))

    const response = await POST(makeReq('identity-target-v1'))

    expect(response.status).toBe(503)
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('unavailable')
  })

  it('拒绝无身份前置条件的请求', async () => {
    const response = await POST(makeReq())

    expect(response.status).toBe(428)
    expect(await response.json()).toEqual({
      error: 'Precondition Required',
      code: SESSION_IDENTITY_PRECONDITION_CODE,
    })
    expect(stopImpersonationMock).not.toHaveBeenCalled()
  })

  it('其它写回失败返回 200 stop-failed，让横幅提示刷新而不是抛传输错误', async () => {
    stopImpersonationMock.mockRejectedValue(new Error('SESSION_REFRESH_INVARIANT'))

    const response = await POST(makeReq('identity-target-v1'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ error: 'stop-failed' })
  })
})
