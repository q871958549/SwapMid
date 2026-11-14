import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SESSION_UNAVAILABLE_RETRY_SCRIPT } from './session-unavailable-retry-script'

describe('session-unavailable retry script', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    document.body.innerHTML = `
      <a id="retry" href="/dashboard" data-idle="重试" data-pending="正在重试…" data-again="仍不可用，可再试一次">重试</a>
      <p id="hint" hidden>请稍候，不要反复点击</p>
    `
    // 与 503 页相同：内联脚本按文本执行（innerHTML 不会跑 <script>）。
    new Function(SESSION_UNAVAILABLE_RETRY_SCRIPT)()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('首次点击进入 pending 且不拦截导航', () => {
    const a = document.getElementById('retry') as HTMLAnchorElement
    const hint = document.getElementById('hint') as HTMLParagraphElement
    const first = new MouseEvent('click', { bubbles: true, cancelable: true })

    expect(a.dispatchEvent(first)).toBe(true)
    expect(a.getAttribute('aria-busy')).toBe('true')
    expect(a.classList.contains('is-pending')).toBe(true)
    expect(a.textContent).toBe('正在重试…')
    expect(hint.hidden).toBe(false)
  })

  it('pending 期间连点被 preventDefault', () => {
    const a = document.getElementById('retry') as HTMLAnchorElement
    a.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    const second = new MouseEvent('click', { bubbles: true, cancelable: true })
    expect(a.dispatchEvent(second)).toBe(false)
    expect(a.textContent).toBe('正在重试…')
  })

  it('8 秒后仍停在本页则恢复可点，文案换成仍不可用', () => {
    const a = document.getElementById('retry') as HTMLAnchorElement
    const hint = document.getElementById('hint') as HTMLParagraphElement
    a.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    vi.advanceTimersByTime(7999)
    expect(a.getAttribute('aria-busy')).toBe('true')

    vi.advanceTimersByTime(1)
    expect(a.getAttribute('aria-busy')).toBeNull()
    expect(a.classList.contains('is-pending')).toBe(false)
    expect(a.textContent).toBe('仍不可用，可再试一次')
    expect(hint.hidden).toBe(true)

    const again = new MouseEvent('click', { bubbles: true, cancelable: true })
    expect(a.dispatchEvent(again)).toBe(true)
    expect(a.textContent).toBe('正在重试…')
  })
})
