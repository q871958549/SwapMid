import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'

vi.mock('@/lib/session', () => ({ deleteSession: vi.fn() }))

import { deleteSession } from '@/lib/session'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { POST } from './route'

const deleteSessionMock = vi.mocked(deleteSession)

function makeReq(identityVersion?: string, userAgent = 'vitest'): NextRequest {
  const headers = new Headers()
  if (identityVersion) headers.set(SESSION_IDENTITY_VERSION_HEADER, identityVersion)
  headers.set('user-agent', userAgent)
  return { headers } as unknown as NextRequest
}

beforeEach(() => {
  vi.clearAllMocks()
  deleteSessionMock.mockResolvedValue({ status: 'deleted' })
})

describe('logout route', () => {
  it('按当前文档身份世代条件退出，并在响应返回后由客户端广播', async () => {
    const response = await POST(makeReq('identity-current'))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ ok: true })
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get('set-cookie')).toBeNull()
    expect(deleteSessionMock).toHaveBeenCalledWith('identity-current')
  })


  it('Redis 不可用时保留会话并返回 tagged 503', async () => {
    deleteSessionMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 3,
    })

    const response = await POST(makeReq('identity-current'))

    expect(response.status).toBe(503)
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('unavailable')
    expect(response.headers.get('retry-after')).toBe('3')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it('拒绝无身份前置条件的跨站表单式退出', async () => {
    const response = await POST(makeReq())

    expect(response.status).toBe(428)
    expect(deleteSessionMock).not.toHaveBeenCalled()
  })

  it('旧标签不得误退新身份，并返回可触发整页自愈的 409', async () => {
    deleteSessionMock.mockResolvedValue({
      status: 'identity-changed',
      identityVersion: 'identity-new',
    })

    const response = await POST(makeReq('identity-old'))

    expect(response.status).toBe(409)
    expect(response.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(response.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-new')
  })

  it('连续会话竞态时保留会话并返回可重试冲突', async () => {
    deleteSessionMock.mockRejectedValue(new Error('SESSION_CONCURRENT_MUTATION'))

    const response = await POST(makeReq('identity-current'))

    expect(response.status).toBe(409)
    expect(await response.json()).toEqual({
      error: 'Conflict',
      code: 'SESSION_CONCURRENT_MUTATION',
    })
  })
})
