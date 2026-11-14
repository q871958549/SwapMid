import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'

vi.mock('@/lib/session', () => ({
  getSessionResult: vi.fn(),
}))

import { getSessionResult } from '@/lib/session'
import { SESSION_IDENTITY_VERSION_QUERY } from '@/lib/session-availability'
import { GET } from './route'

const getSessionResultMock = vi.mocked(getSessionResult)
const SESSION = {
  identityVersion: 'identity-current',
  accessToken: 'AT-1',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  accessTokenExpiresAt: Date.now() + 3_600_000,
}

function makeReq(
  redirect?: string,
  expectedIdentityVersion?: string,
  userAgent = 'vitest',
  cookieValues: Record<string, string> = {},
): NextRequest {
  const searchParams = new URLSearchParams()
  if (redirect !== undefined) searchParams.set('redirect', redirect)
  if (expectedIdentityVersion !== undefined) {
    searchParams.set(
      SESSION_IDENTITY_VERSION_QUERY,
      expectedIdentityVersion,
    )
  }
  return {
    nextUrl: { searchParams },
    headers: new Headers({ 'user-agent': userAgent }),
    cookies: {
      get: (name: string) => name in cookieValues
        ? { name, value: cookieValues[name] }
        : undefined,
    },
  } as unknown as NextRequest
}

beforeEach(() => {
  vi.clearAllMocks()
  getSessionResultMock.mockResolvedValue({ status: 'missing' })
})

describe('session-expired route', () => {
  it('服务端状态确认缺失时导航登录，但被动流程不写 cookie', async () => {
    const response = await GET(makeReq('/data-integration/pipelines?tab=active'))

    expect(response.status).toBe(307)
    expect(response.headers.get('location')).toBe(
      '/login?redirect=%2Fdata-integration%2Fpipelines%3Ftab%3Dactive',
    )
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it('即使身份版本匹配也不凭导航请求删除有效会话', async () => {
    getSessionResultMock.mockResolvedValue({ status: 'found', session: SESSION })

    const response = await GET(makeReq('/dashboard', 'identity-current'))

    expect(response.headers.get('location')).toBe('/dashboard')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it('迟到的旧 401 不得删除后来登录或切换的新身份', async () => {
    getSessionResultMock.mockResolvedValue({ status: 'found', session: SESSION })

    const response = await GET(makeReq('/dashboard', 'identity-old'))

    expect(response.status).toBe(307)
    expect(response.headers.get('location')).toBe('/dashboard')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it('无身份版本的跨站顶级导航不得强制登出有效会话', async () => {
    getSessionResultMock.mockResolvedValue({ status: 'found', session: SESSION })

    const response = await GET(makeReq('/profile'))

    expect(response.status).toBe(307)
    expect(response.headers.get('location')).toBe('/profile')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it('会话存储不可用时保留 cookie 并转入可重试故障页', async () => {
    getSessionResultMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 3,
    })

    const response = await GET(makeReq('/profile', 'identity-current'))

    expect(response.status).toBe(307)
    expect(response.headers.get('location'))
      .toBe('/api/auth/session-unavailable?redirect=%2Fprofile')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it.each([
    '//evil.example/steal',
    '/\\evil.example/steal',
    '/safe\n//evil.example',
  ])('拒绝有解析歧义的外部跳转：%s', async (redirect) => {
    const response = await GET(makeReq(redirect))

    expect(response.headers.get('location')).toBe('/login')
  })

})
