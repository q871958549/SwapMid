import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'

const mocks = vi.hoisted(() => ({
  getValidSessionResult: vi.fn(),
  startImpersonation: vi.fn(),
  fetchAbpUserConfig: vi.fn(),
  serverBackendFetch: vi.fn(),
  getSessionIdentityVersion: vi.fn(
    (session: { identityVersion?: string }) => session.identityVersion ?? 'legacy',
  ),
}))

vi.mock('@/lib/session', () => ({
  getValidSessionResult: mocks.getValidSessionResult,
  startImpersonation: mocks.startImpersonation,
  SessionIdentityChangedError: class SessionIdentityChangedError extends Error {
    constructor(readonly identityVersion: string) {
      super('SESSION_IDENTITY_CHANGED')
      this.name = 'SessionIdentityChangedError'
    }
  },
}))
vi.mock('@/lib/abp-config', () => ({
  fetchAbpUserConfig: mocks.fetchAbpUserConfig,
}))
vi.mock('@/lib/session-identity', () => ({
  getSessionIdentityVersion: mocks.getSessionIdentityVersion,
}))
vi.mock('@/lib/server-backend-fetch', () => ({
  serverBackendFetch: mocks.serverBackendFetch,
  SERVER_BACKEND_DEADLINE_MS: { authentication: 15_000 },
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES: { oauthToken: 64 * 1024 },
}))

vi.spyOn(console, 'error').mockImplementation(() => {})

import { SessionIdentityChangedError, startImpersonation } from '@/lib/session'
import {
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { POST } from './route'

const startImpersonationMock = vi.mocked(startImpersonation)

const SESSION = {
  identityVersion: 'identity-admin',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  accessToken: 'admin-access-token',
  accessTokenExpiresAt: Date.now() + 60_000,
}

function makeReq(identityVersion?: string, body: unknown = { targetUserId: 'target-id' }): NextRequest {
  return {
    headers: new Headers(
      identityVersion
        ? { [SESSION_IDENTITY_VERSION_HEADER]: identityVersion }
        : undefined,
    ),
    json: async () => body,
  } as unknown as NextRequest
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.getValidSessionResult.mockResolvedValue({
    status: 'found',
    session: SESSION,
  })
  mocks.fetchAbpUserConfig.mockResolvedValue({
    userId: 'target-id',
    userName: 'target',
    name: '目标用户',
    grantedPolicies: { 'SwapMid.Foo': true },
  })
  startImpersonationMock.mockResolvedValue({
    ...SESSION,
    identityVersion: 'identity-target',
    userName: 'target',
  })
  mocks.serverBackendFetch.mockResolvedValue({
    ok: true,
    json: async () => ({ access_token: 'target-access-token', expires_in: 3600 }),
  })
})

describe('start-impersonation route', () => {
  it('按当前文档身份世代换 token 并 CAS 写入，响应不含 token', async () => {
    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(200)
    const payload = await response.json()
    expect(payload).toEqual({
      ok: true,
      identityVersion: 'identity-target',
    })
    expect(JSON.stringify(payload)).not.toContain('access_token')
    expect(JSON.stringify(payload)).not.toContain('target-access-token')
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-target')

    expect(mocks.serverBackendFetch).toHaveBeenCalledOnce()
    const [url, init] = mocks.serverBackendFetch.mock.calls[0] as [string, RequestInit]
    expect(url).toMatch(/\/connect\/token$/)
    expect(String(init.body)).toContain('grant_type=Impersonation')
    expect(String(init.body)).toContain('access_token=admin-access-token')
    expect(String(init.body)).toContain('target_user_id=target-id')
    expect(String(init.body)).not.toContain('offline_access')
    expect(startImpersonationMock).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: 'target-id',
        accessToken: 'target-access-token',
      }),
      'identity-admin',
    )
  })

  it('身份已变时返回 200 identityChanged，不换 token，以免 Guard 停在账号页刷新', async () => {
    mocks.getValidSessionResult.mockResolvedValue({
      status: 'identity-changed',
      session: { ...SESSION, identityVersion: 'identity-admin-v3' },
    })

    const response = await POST(makeReq('identity-stale-v0'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({
      identityChanged: true,
      identityVersion: 'identity-admin-v3',
    })
    expect(response.headers.get(SESSION_STATE_HEADER)).toBeNull()
    expect(mocks.serverBackendFetch).not.toHaveBeenCalled()
    expect(startImpersonationMock).not.toHaveBeenCalled()
  })

  it('写入阶段 CAS 冲突同样返回 200 identityChanged', async () => {
    startImpersonationMock.mockRejectedValue(
      new SessionIdentityChangedError('identity-other-v2'),
    )

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({
      identityChanged: true,
      identityVersion: 'identity-other-v2',
    })
  })

  it('会话已消失返回 tagged 401，且不请求 token', async () => {
    mocks.getValidSessionResult.mockResolvedValue({ status: 'missing' })

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(401)
    expect(await response.json()).toEqual({ error: 'session-expired' })
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('missing')
    expect(mocks.serverBackendFetch).not.toHaveBeenCalled()
  })

  it('存储不可用返回 tagged 503', async () => {
    mocks.getValidSessionResult.mockResolvedValue({ status: 'unavailable' })

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(503)
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('unavailable')
    expect(mocks.serverBackendFetch).not.toHaveBeenCalled()
  })

  it('拒绝无身份前置条件的请求', async () => {
    const response = await POST(makeReq())

    expect(response.status).toBe(428)
    expect(await response.json()).toEqual({
      error: 'Precondition Required',
      code: SESSION_IDENTITY_PRECONDITION_CODE,
    })
    expect(mocks.getValidSessionResult).not.toHaveBeenCalled()
  })

  it('已在模拟态时不换 token', async () => {
    mocks.getValidSessionResult.mockResolvedValue({
      status: 'found',
      session: { ...SESSION, impersonator: { userName: 'admin' } },
    })

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ error: 'already-impersonating' })
    expect(mocks.serverBackendFetch).not.toHaveBeenCalled()
  })

  it('grant 拒绝时返回 200 failed，且日志不含 access_token', async () => {
    mocks.serverBackendFetch.mockResolvedValue({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({ error: 'invalid_grant' }),
    })

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ error: 'failed' })
    expect(startImpersonationMock).not.toHaveBeenCalled()
  })

  it('token 网络失败不重试，沿用 network 语义', async () => {
    mocks.serverBackendFetch.mockRejectedValue(new Error('network'))

    const response = await POST(makeReq('identity-admin'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ error: 'network' })
    expect(startImpersonationMock).not.toHaveBeenCalled()
  })

  it('非法目标账号不请求 token', async () => {
    const response = await POST(makeReq('identity-admin', { targetUserId: '   ' }))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ error: 'failed' })
    expect(mocks.getValidSessionResult).not.toHaveBeenCalled()
    expect(mocks.serverBackendFetch).not.toHaveBeenCalled()
  })
})
