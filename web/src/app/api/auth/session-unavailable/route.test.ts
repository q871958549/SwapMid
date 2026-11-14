import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'
import {
  SESSION_UNAVAILABLE_RETRY_SCRIPT,
  sessionUnavailableScriptSrcHash,
} from '../session-unavailable-retry-script'

const routeMocks = vi.hoisted(() => ({
  cookies: vi.fn(),
  translations: {
    title: '服务暂不可用',
    unavailableDescription: '会话服务暂时无法连接，请稍后重试。',
    retry: '重试',
    retrying: '正在重试…',
    retryAgain: '仍不可用，可再试一次',
    retryHint: '请稍候，不要反复点击',
  } as Record<string, string>,
}))

vi.mock('next/headers', () => ({ cookies: routeMocks.cookies }))
vi.mock('next-intl/server', () => ({
  getLocale: vi.fn(async () => 'zh-Hans'),
  getTranslations: vi.fn(async () =>
    (key: string) => routeMocks.translations[key]),
}))

import { GET } from './route'

function makeReq(redirect?: string): NextRequest {
  const searchParams = new URLSearchParams()
  if (redirect !== undefined) searchParams.set('redirect', redirect)
  return {
    nextUrl: { searchParams },
  } as unknown as NextRequest
}

beforeEach(() => {
  vi.clearAllMocks()
  routeMocks.translations.title = '服务暂不可用'
  routeMocks.translations.unavailableDescription = '会话服务暂时无法连接，请稍后重试。'
  routeMocks.translations.retry = '重试'
  routeMocks.translations.retrying = '正在重试…'
  routeMocks.translations.retryAgain = '仍不可用，可再试一次'
  routeMocks.translations.retryHint = '请稍候，不要反复点击'
})

describe('session-unavailable route', () => {
  it('返回可重试且不可缓存的 503，不删除或覆盖会话 cookie', async () => {
    const response = await GET(makeReq())
    const csp = response.headers.get('content-security-policy') ?? ''

    expect(response.status).toBe(503)
    expect(response.headers.get('retry-after')).toBe('2')
    expect(response.headers.get('cache-control')).toBe('private, no-store')
    expect(response.headers.get('x-swapmid-session-state')).toBe('unavailable')
    expect(response.headers.get('set-cookie')).toBeNull()
    expect(response.headers.get('x-content-type-options')).toBe('nosniff')
    expect(response.headers.get('referrer-policy')).toBe('no-referrer')
    expect(csp).toContain("default-src 'none'")
    expect(csp).toContain(`script-src ${sessionUnavailableScriptSrcHash()}`)
    expect(csp).toMatch(/style-src 'unsafe-inline'/)
    expect(csp).not.toMatch(/script-src[^;]*unsafe-inline/)
    expect(routeMocks.cookies).not.toHaveBeenCalled()
  })

  it('重试链接带 pending 文案，脚本与 CSP 哈希同源', async () => {
    const html = await (await GET(makeReq('/quality-document/stamp'))).text()

    expect(html).toContain('id="retry"')
    expect(html).toContain('href="/quality-document/stamp"')
    expect(html).toContain('data-idle="重试"')
    expect(html).toContain('data-pending="正在重试…"')
    expect(html).toContain('data-again="仍不可用，可再试一次"')
    expect(html).toContain('id="hint" hidden')
    expect(html).toContain('a.is-pending')
    expect(html).toContain(`<script>${SESSION_UNAVAILABLE_RETRY_SCRIPT}</script>`)
    expect(html).not.toContain('</script></script>')
  })

  it('保留站内回跳地址，并拒绝双斜杠外部跳转', async () => {
    const internal = await GET(
      makeReq('/reporting/daily-output?tab=detail&filter=未完成'),
    )
    const unsafe = await GET(makeReq('//evil.example/steal'))

    expect(await internal.text()).toContain(
      'href="/reporting/daily-output?tab=detail&amp;filter=未完成"',
    )
    expect(await unsafe.text()).toContain('href="/dashboard"')
  })

  it.each([
    '/\\evil.example/steal',
    '/safe\n//evil.example',
  ])('拒绝浏览器可解释为外域的路径：%s', async (redirect) => {
    const response = await GET(makeReq(redirect))

    expect(await response.text()).toContain('href="/dashboard"')
  })

  it('转义翻译文案和回跳地址后再写入 HTML', async () => {
    routeMocks.translations.title = '<img src=x onerror="alert(1)"> & 标题'
    routeMocks.translations.unavailableDescription = `描述 'quoted' <script>bad()</script>`
    routeMocks.translations.retry = '重试 >'
    routeMocks.translations.retrying = '正在<script>x</script>'
    routeMocks.translations.retryAgain = '再试 "'
    routeMocks.translations.retryHint = "别点 ' 了"

    const response = await GET(makeReq('/search?q="><svg onload=alert(1)>'))
    const html = await response.text()

    expect(html).toContain(
      '&lt;img src=x onerror=&quot;alert(1)&quot;&gt; &amp; 标题',
    )
    expect(html).toContain(
      '描述 &#39;quoted&#39; &lt;script&gt;bad()&lt;/script&gt;',
    )
    expect(html).toContain('data-idle="重试 &gt;"')
    expect(html).toContain('data-pending="正在&lt;script&gt;x&lt;/script&gt;"')
    expect(html).toContain('data-again="再试 &quot;"')
    expect(html).toContain('别点 &#39; 了')
    expect(html).toContain(
      'href="/search?q=&quot;&gt;&lt;svg onload=alert(1)&gt;"',
    )
    expect(html).not.toContain('<script>bad()</script>')
    expect(html).not.toContain('<img src=x')
  })
})
