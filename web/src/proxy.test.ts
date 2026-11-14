import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NextRequest } from 'next/server'

vi.mock('@/lib/session-store', () => ({
  getStoredSession: vi.fn(),
  isValidSessionId: (id: string) => /^[0-9a-f-]{36}$/i.test(id),
}))

import { getStoredSession } from '@/lib/session-store'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { config, proxy } from './proxy'

const getStoredSessionMock = vi.mocked(getStoredSession)
const VALID_SID = '123e4567-e89b-12d3-a456-426614174000'

function makeRequest(
  path: string,
  options: { sid?: string; headers?: Record<string, string> } = {},
): NextRequest {
  const headers = new Headers(options.headers)
  if (options.sid) {
    headers.set('cookie', `swapmid_sid=${options.sid}`)
  }
  return new NextRequest(new URL(path, 'https://swapmid.example.com'), {
    headers,
  })
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('proxy middleware', () => {
  it('excludes /api/proxy from middleware buffering matcher', () => {
    expect(config.matcher[0]).toContain('api/proxy')
  })

  it('redirects unauthenticated protected requests to /login with redirect query', async () => {
    const res = await proxy(makeRequest('/data-integration/pipelines?page=2'))
    expect(res.status).toBe(307)
    expect(res.headers.get('location')).toBe(
      'https://swapmid.example.com/login?redirect=%2Fdata-integration%2Fpipelines%3Fpage%3D2',
    )
  })

  it('redirects authenticated /login requests to /dashboard and passes through stale cookies', async () => {
    getStoredSessionMock.mockResolvedValueOnce({
      status: 'found',
      session: {
        identityVersion: 'id-1',
        accessToken: 'at',
        accessTokenExpiresAt: Date.now() + 60_000,
        userName: 'admin',
        name: 'Administrator',
        grantedPolicies: {},
      },
    })
    const active = await proxy(makeRequest('/login', { sid: VALID_SID }))
    expect(active.status).toBe(307)
    expect(active.headers.get('location')).toBe('https://swapmid.example.com/dashboard')

    getStoredSessionMock.mockResolvedValueOnce({ status: 'missing' })
    const stale = await proxy(makeRequest('/login', { sid: VALID_SID }))
    expect(stale.status).toBe(200)
    expect(stale.headers.get('set-cookie')).toBeNull()
  })

  it('returns 503 on /login when Redis session store is unavailable', async () => {
    getStoredSessionMock.mockResolvedValueOnce({
      status: 'unavailable',
      retryAfterSeconds: 3,
    })
    const res = await proxy(makeRequest('/login', { sid: VALID_SID }))
    expect(res.status).toBe(503)
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('unavailable')
  })

  it('fences RSC partial requests with 409 when session identity version changes', async () => {
    getStoredSessionMock.mockResolvedValueOnce({
      status: 'found',
      session: {
        identityVersion: 'identity-new-version-0001',
        accessToken: 'at',
        accessTokenExpiresAt: Date.now() + 60_000,
        userName: 'admin',
        name: 'Administrator',
        grantedPolicies: {},
      },
    })
    const res = await proxy(
      makeRequest('/system/scheduled-jobs', {
        sid: VALID_SID,
        headers: {
          rsc: '1',
          [SESSION_IDENTITY_VERSION_HEADER]: 'identity-old-version-0001',
        },
      }),
    )
    expect(res.status).toBe(409)
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER)).toBe(
      'identity-new-version-0001',
    )
  })
})
