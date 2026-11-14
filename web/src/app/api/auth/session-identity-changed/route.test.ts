import { describe, expect, it } from 'vitest'
import type { NextRequest } from 'next/server'
import { GET } from './route'

function makeReq(redirect?: string): NextRequest {
  const searchParams = new URLSearchParams()
  if (redirect !== undefined) searchParams.set('redirect', redirect)
  return { nextUrl: { searchParams } } as unknown as NextRequest
}

describe('session-identity-changed route', () => {
  it('保留有效会话并整页回到安全站内路径', async () => {
    const response = await GET(makeReq('/project-management/samples?tab=mine'))

    expect(response.status).toBe(307)
    expect(response.headers.get('location')).toBe(
      '/project-management/samples?tab=mine',
    )
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get('set-cookie')).toBeNull()
  })

  it.each([
    undefined,
    '//evil.example/steal',
    '/\\evil.example/steal',
    '/safe\n//evil.example',
  ])('缺省或歧义地址回到 dashboard：%s', async (redirect) => {
    const response = await GET(makeReq(redirect))

    expect(response.headers.get('location')).toBe('/dashboard')
  })
})
