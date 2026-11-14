import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import {
  requestStartImpersonation,
  requestStopImpersonation,
  START_IMPERSONATION_PATH,
  STOP_IMPERSONATION_PATH,
} from './impersonation-client'

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
})

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
}

describe('requestStartImpersonation', () => {
  it('携带当前文档身份世代与目标账号 POST 独立进入入口', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      ok: true,
      identityVersion: 'identity-target-v1',
    }))

    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({
        status: 'started',
        identityVersion: 'identity-target-v1',
      })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(START_IMPERSONATION_PATH)
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ targetUserId: 'target-id' }))
    expect(new Headers(init.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-admin')
  })

  it('服务端已换身份时把 identityChanged 当成成功，交给账号页硬跳', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      identityChanged: true,
      identityVersion: 'identity-other-v2',
    }))

    await expect(requestStartImpersonation('target-id', 'identity-stale-v0'))
      .resolves.toEqual({
        status: 'started',
        identityVersion: 'identity-other-v2',
      })
  })

  it('已在模拟态映射为 already-impersonating', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ error: 'already-impersonating' }))

    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'already-impersonating' })
  })

  it('tagged 503 映射为 unavailable，避免再弹模拟失败', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: 'unavailable' }), {
      status: 503,
      headers: { [SESSION_STATE_HEADER]: 'unavailable' },
    }))

    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'unavailable' })
  })

  it('缺会话或无身份头映射为 session-expired', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }))
    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'session-expired' })

    fetchMock.mockResolvedValue(new Response(null, { status: 428 }))
    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'session-expired' })
  })

  it('非 JSON 映射为 network，业务失败映射为 failed', async () => {
    fetchMock.mockResolvedValue(new Response('<html>', { status: 200 }))
    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'network' })

    fetchMock.mockResolvedValue(jsonResponse({ error: 'failed' }))
    await expect(requestStartImpersonation('target-id', 'identity-admin'))
      .resolves.toEqual({ status: 'failed', errorKey: 'failed' })
  })
})

describe('requestStopImpersonation', () => {
  it('携带当前文档身份世代 POST 独立切回入口', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      ok: true,
      identityVersion: 'identity-admin-v2',
    }))

    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({
        status: 'switched',
        identityVersion: 'identity-admin-v2',
      })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(STOP_IMPERSONATION_PATH)
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-target-v1')
  })

  it('服务端已切回时把 identityChanged 当成成功，交给横幅硬跳', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      identityChanged: true,
      identityVersion: 'identity-admin-v3',
    }))

    await expect(requestStopImpersonation('identity-stale-v0'))
      .resolves.toEqual({
        status: 'switched',
        identityVersion: 'identity-admin-v3',
      })
  })

  it('tagged 503 映射为 unavailable，避免再弹切回失败', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ error: 'unavailable' }), {
      status: 503,
      headers: { [SESSION_STATE_HEADER]: 'unavailable' },
    }))

    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({ status: 'failed', errorKey: 'unavailable' })
  })

  it('缺会话或无身份头映射为 session-expired', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }))
    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({ status: 'failed', errorKey: 'session-expired' })

    fetchMock.mockResolvedValue(new Response(null, { status: 428 }))
    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({ status: 'failed', errorKey: 'session-expired' })
  })

  it('非 JSON 或业务失败映射为 stop-failed', async () => {
    fetchMock.mockResolvedValue(new Response('<html>', { status: 200 }))
    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({ status: 'failed', errorKey: 'stop-failed' })

    fetchMock.mockResolvedValue(jsonResponse({ error: 'stop-failed' }))
    await expect(requestStopImpersonation('identity-target-v1'))
      .resolves.toEqual({ status: 'failed', errorKey: 'stop-failed' })
  })
})
