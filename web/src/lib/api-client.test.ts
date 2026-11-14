import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => {
  class RedirectSignal extends Error {
    constructor(readonly location: string) {
      super(`redirect:${location}`)
    }
  }

  return {
    RedirectSignal,
    cookies: vi.fn(),
    headers: vi.fn(),
    getValidSessionResult: vi.fn(),
    invalidateSessionIfUnchanged: vi.fn(),
    redirectSessionFailure: vi.fn(),
    redirectSessionIdentityChanged: vi.fn(),
    unstableRethrow: vi.fn((error: unknown) => {
      if (error instanceof RedirectSignal) throw error
    }),
  }
})

vi.mock('next/navigation', () => ({
  unstable_rethrow: mocks.unstableRethrow,
}))

vi.mock('next/headers', () => ({
  cookies: mocks.cookies,
  headers: mocks.headers,
}))

vi.mock('@/lib/session', () => ({
  getValidSessionResult: mocks.getValidSessionResult,
  invalidateSessionIfUnchanged: mocks.invalidateSessionIfUnchanged,
}))

vi.mock('@/lib/session-navigation', () => ({
  redirectSessionFailure: mocks.redirectSessionFailure,
  redirectSessionIdentityChanged: mocks.redirectSessionIdentityChanged,
}))

import { apiFetch } from './api-client'

const CURRENT_PATH = '/reporting/daily-output?tab=待处理&from=/dashboard'
const SESSION = {
  identityVersion: 'identity-current',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  accessTokenExpiresAt: Date.now() + 60_000,
}

function redirectLocation(
  failure: { status: 'missing' } | { status: 'unavailable'; retryAfterSeconds: number },
  returnTo?: string,
): string {
  const endpoint = failure.status === 'missing'
    ? '/api/auth/session-expired'
    : '/api/auth/session-unavailable'
  return returnTo
    ? `${endpoint}?redirect=${encodeURIComponent(returnTo)}`
    : endpoint
}

beforeEach(() => {
  vi.clearAllMocks()

  mocks.headers.mockResolvedValue({
    get: vi.fn((name: string) => name === 'x-swapmid-pathname' ? CURRENT_PATH : null),
  })
  mocks.cookies.mockResolvedValue({
    get: vi.fn((name: string) => name === 'swapmid_language'
      ? { value: 'zh-Hans' }
      : undefined),
  })
  mocks.redirectSessionFailure.mockImplementation(
    (
      failure:
        | { status: 'missing' }
        | { status: 'unavailable'; retryAfterSeconds: number },
      returnTo?: string,
    ) => {
      throw new mocks.RedirectSignal(redirectLocation(failure, returnTo))
    },
  )
  mocks.redirectSessionIdentityChanged.mockImplementation((returnTo?: string) => {
    throw new mocks.RedirectSignal(
      `/api/auth/session-identity-changed?redirect=${encodeURIComponent(returnTo ?? '')}`,
    )
  })
  mocks.invalidateSessionIfUnchanged.mockResolvedValue({
    status: 'invalidated',
  })
})

describe('apiFetch session 三态', () => {
  it('found 时携带服务端 Bearer token，并解析 JSON 响应', async () => {
    mocks.getValidSessionResult.mockResolvedValue({
      status: 'found',
      session: SESSION,
    })
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ value: 42 }),
      {
        status: 200,
        headers: { 'content-type': 'application/json; charset=utf-8' },
      },
    ))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch<{ value: number }>('/reports', {
      headers: { 'X-Correlation-Id': 'correlation-1' },
    })).resolves.toEqual({ value: 42 })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toMatch(/\/api\/v1\/reports$/)
    expect(init.headers).toMatchObject({
      Authorization: 'Bearer access-token',
      'Content-Type': 'application/json',
      'Accept-Language': 'zh-Hans',
      'X-Correlation-Id': 'correlation-1',
    })
    expect(mocks.redirectSessionFailure).not.toHaveBeenCalled()
  })

  it('missing 时走 session-expired，并完整编码当前路径', async () => {
    mocks.getValidSessionResult.mockResolvedValue({ status: 'missing' })
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    const rejection = apiFetch('/reports').catch(() => ({ swallowed: true }))

    await expect(rejection).rejects.toMatchObject({
      location:
        '/api/auth/session-expired'
        + '?redirect=%2Freporting%2Fdaily-output'
        + '%3Ftab%3D%E5%BE%85%E5%A4%84%E7%90%86%26from%3D%2Fdashboard',
    })
    expect(mocks.redirectSessionFailure).toHaveBeenCalledWith(
      { status: 'missing' },
      CURRENT_PATH,
    )
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('unavailable 时走 session-unavailable，且不发起后端请求', async () => {
    const unavailable = { status: 'unavailable' as const, retryAfterSeconds: 2 }
    mocks.getValidSessionResult.mockResolvedValue(unavailable)
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    const rejection = apiFetch('/reports').catch(() => ({ swallowed: true }))

    await expect(rejection).rejects.toMatchObject({
      location:
        '/api/auth/session-unavailable'
        + '?redirect=%2Freporting%2Fdaily-output'
        + '%3Ftab%3D%E5%BE%85%E5%A4%84%E7%90%86%26from%3D%2Fdashboard',
    })
    expect(mocks.redirectSessionFailure).toHaveBeenCalledWith(
      unavailable,
      CURRENT_PATH,
    )
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('后端返回 401 时明确按 missing 跳转', async () => {
    mocks.getValidSessionResult.mockResolvedValue({
      status: 'found',
      session: SESSION,
    })
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
    vi.stubGlobal('fetch', fetchMock)

    const rejection = apiFetch('/api/identity/users')
      .then((value) => value)
      .catch(() => ({ swallowed: true }))

    await expect(rejection).rejects.toMatchObject({
      location:
        '/api/auth/session-expired'
        + '?redirect=%2Freporting%2Fdaily-output'
        + '%3Ftab%3D%E5%BE%85%E5%A4%84%E7%90%86%26from%3D%2Fdashboard',
    })
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(mocks.redirectSessionFailure).toHaveBeenCalledWith(
      { status: 'missing' },
      CURRENT_PATH,
      'identity-current',
    )
  })

  it('迟到 401 遇到同身份 token 刷新胜者时不清会话，改为整页重建', async () => {
    mocks.getValidSessionResult.mockResolvedValue({
      status: 'found',
      session: SESSION,
    })
    mocks.invalidateSessionIfUnchanged.mockResolvedValue({
      status: 'session-updated',
      session: { ...SESSION, accessToken: 'access-token-2' },
    })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(
      apiFetch('/reports').catch(() => ({ swallowed: true })),
    ).rejects.toMatchObject({
      location:
        '/api/auth/session-identity-changed'
        + '?redirect=%2Freporting%2Fdaily-output'
        + '%3Ftab%3D%E5%BE%85%E5%A4%84%E7%90%86%26from%3D%2Fdashboard',
    })
    expect(mocks.redirectSessionFailure).not.toHaveBeenCalled()
    expect(mocks.redirectSessionIdentityChanged)
      .toHaveBeenCalledWith(CURRENT_PATH)
  })
})
