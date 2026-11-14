import { afterEach, describe, expect, it, vi } from 'vitest'
import { navUsageOf, reportNavUsage } from './nav-usage'

describe('navUsageOf', () => {
  it('导航页按主归属给出域、组、项；子路径归一到所属项', () => {
    expect(navUsageOf('/data-integration/pipelines')).toEqual({
      domain: 'integration',
      group: 'data',
      href: '/data-integration/pipelines',
    })
    expect(navUsageOf('/system/scheduled-jobs/executions')?.href).toBe('/system/scheduled-jobs')
  })

  it('非导航页（仪表盘、个人中心、未知路径）不上报', () => {
    expect(navUsageOf('/dashboard')).toBeNull()
    expect(navUsageOf('/profile')).toBeNull()
    expect(navUsageOf('/no-such-page')).toBeNull()
  })
})

describe('reportNavUsage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('POST 域/组/项，不带查询参数；同一页 5 秒内只报一次', () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })))
    vi.stubGlobal('fetch', fetchMock)

    reportNavUsage('/data-integration/sources', 1_000_000)
    reportNavUsage('/data-integration/sources', 1_003_000)
    reportNavUsage('/data-integration/sources', 1_006_000)

    expect(fetchMock).toHaveBeenCalledTimes(2)
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/proxy/nav-usage')
    expect(JSON.parse(init.body as string)).toEqual({
      domain: 'integration',
      group: 'data',
      href: '/data-integration/sources',
    })
  })
})
