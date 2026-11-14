import { beforeEach, describe, expect, it, vi } from 'vitest'

const redirectMock = vi.hoisted(() => vi.fn())

vi.mock('next/navigation', () => ({ redirect: redirectMock }))

import { redirectSessionFailure } from './session-navigation'

beforeEach(() => {
  vi.clearAllMocks()
})

describe('redirectSessionFailure', () => {
  it('missing 只导航到 session-expired，并完整编码回跳地址', () => {
    redirectSessionFailure(
      { status: 'missing' },
      '/project-management/samples?tab=待审批&from=/dashboard',
    )

    expect(redirectMock).toHaveBeenCalledOnce()
    expect(redirectMock).toHaveBeenCalledWith(
      '/api/auth/session-expired'
      + '?redirect=%2Fproject-management%2Fsamples'
      + '%3Ftab%3D%E5%BE%85%E5%AE%A1%E6%89%B9%26from%3D%2Fdashboard',
    )
  })

  it('后端 401 携带发起请求的身份版本，迟到响应不得误删新会话', () => {
    redirectSessionFailure(
      { status: 'missing' },
      '/dashboard',
      'identity-old',
    )

    expect(redirectMock).toHaveBeenCalledWith(
      '/api/auth/session-expired'
      + '?redirect=%2Fdashboard&_swapmidIdentityVersion=identity-old',
    )
  })

  it('unavailable 只导航到 session-unavailable，并完整编码回跳地址', () => {
    redirectSessionFailure(
      { status: 'unavailable', retryAfterSeconds: 2 },
      '/reporting/daily-output?filter=a b&view=list',
    )

    expect(redirectMock).toHaveBeenCalledOnce()
    expect(redirectMock).toHaveBeenCalledWith(
      '/api/auth/session-unavailable'
      + '?redirect=%2Freporting%2Fdaily-output%3Ffilter%3Da%20b%26view%3Dlist',
    )
  })

  it('未提供回跳地址时不附加空 redirect 参数', () => {
    redirectSessionFailure({ status: 'missing' })

    expect(redirectMock).toHaveBeenCalledWith('/api/auth/session-expired')
  })

  it('不把反斜杠歧义路径带入两级重定向', () => {
    redirectSessionFailure(
      { status: 'unavailable', retryAfterSeconds: 2 },
      '/\\evil.example/steal',
    )

    expect(redirectMock).toHaveBeenCalledWith('/api/auth/session-unavailable')
  })
})
