import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import {
  authenticatedBrowserStoragePolicies,
  createAuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'
import { ScrollRestorer } from './scroll-restorer'

// usePathname 用可控 mock：各用例按需 mockReturnValue 切换路径，模拟路由切换触发 rerender
// （组件依赖 pathname 变化驱动 effect 重跑，不能只在挂载时固定一个值）。
const mockUsePathname = vi.fn<() => string>()
vi.mock('next/navigation', () => ({
  usePathname: () => mockUsePathname(),
}))

const THROTTLE_MS = 150
const IDENTITY_VERSION = 'identity-scroll-tests'
const STORAGE_PRINCIPAL_ID = 'sp1-scroll-tests'

interface StoredScrollPosition {
  top: number
  touchedAt: number
}

type StoredScrollMap = Record<string, StoredScrollPosition>

const isStoredScrollMap = (value: unknown): value is StoredScrollMap =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

let scopedStorage: ReturnType<typeof createAuthenticatedBrowserStorage>

function withIdentity(children: React.ReactNode) {
  return (
    <SessionIdentityProvider
      identityVersion={IDENTITY_VERSION}
      storagePrincipalId={STORAGE_PRINCIPAL_ID}
    >
      {children}
    </SessionIdentityProvider>
  )
}

function readStoredScrollMap(): StoredScrollMap | null {
  return scopedStorage.read(
    authenticatedBrowserStoragePolicies.scroll,
    isStoredScrollMap,
  )
}

function seedScrollMap(value: StoredScrollMap): void {
  scopedStorage.write(authenticatedBrowserStoragePolicies.scroll, value)
}

// rAF 手动排队：组件用「双 requestAnimationFrame」做恢复，jsdom 原生 rAF 是异步定时器，
// 难以精确卡在「第一帧已跑、第二帧未跑」这个 restoringRef 竞态抑制窗口的中间态。
// 用队列 + 手动 flushRaf() 替代：每调一次 flushRaf() 精确推进一帧，断言可以插在帧间。
let rafQueue: FrameRequestCallback[] = []
function mockRequestAnimationFrame(cb: FrameRequestCallback): number {
  rafQueue.push(cb)
  return rafQueue.length
}
function flushRaf() {
  const pending = rafQueue
  rafQueue = []
  pending.forEach(cb => cb(0))
}

function renderScrollRestorer() {
  render(withIdentity(
    <>
      <div data-scroll-container data-testid="main" />
      <ScrollRestorer />
    </>,
  ))
  return screen.getByTestId('main')
}

// 注意：这里刻意不用 vi.stubGlobal（配置里 unstubGlobals:true 会在框架内置的 afterEach 里
// 自动还原，但其执行时机早于 setup 文件里 RTL 的 cleanup()——组件卸载时的 effect cleanup 调用
// 裸标识符 cancelAnimationFrame 会因此撞上 `ReferenceError: cancelAnimationFrame is not defined`。
// 改为手动保存/还原，并把还原放在本文件自己的 afterEach（注册晚于 setup.ts 的 afterEach，
// 保证 RTL 卸载时 mock 仍在）。
let originalRAF: typeof globalThis.requestAnimationFrame
let originalCAF: typeof globalThis.cancelAnimationFrame

beforeEach(() => {
  window.sessionStorage.clear()
  rafQueue = []
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-07-27T12:00:00Z'))
  scopedStorage = createAuthenticatedBrowserStorage({
    identityVersion: IDENTITY_VERSION,
    storagePrincipalId: STORAGE_PRINCIPAL_ID,
  })
  originalRAF = globalThis.requestAnimationFrame
  originalCAF = globalThis.cancelAnimationFrame
  globalThis.requestAnimationFrame = mockRequestAnimationFrame as typeof globalThis.requestAnimationFrame
  globalThis.cancelAnimationFrame = vi.fn()
})

afterEach(() => {
  vi.useRealTimers()
  globalThis.requestAnimationFrame = originalRAF
  globalThis.cancelAnimationFrame = originalCAF
})

describe('ScrollRestorer · 记录（节流）', () => {
  it('滚动后节流窗口到期 → sessionStorage 记录 {pathname: scrollTop}', () => {
    mockUsePathname.mockReturnValue('/page-a')
    const main = renderScrollRestorer()
    flushRaf()
    flushRaf() // 完成挂载时的双帧恢复（无存量值，置 0），解除 restoringRef 抑制

    main.scrollTop = 240
    main.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(THROTTLE_MS)

    expect(readStoredScrollMap()).toEqual({
      '/page-a': {
        top: 240,
        touchedAt: Date.now(),
      },
    })
  })

  it('节流窗口内多次滚动只落盘一次，且是窗口内最新值', () => {
    mockUsePathname.mockReturnValue('/page-a')
    const main = renderScrollRestorer()
    flushRaf()
    flushRaf()

    main.scrollTop = 100
    main.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(50)
    main.scrollTop = 200
    main.dispatchEvent(new Event('scroll')) // 仍在同一节流窗口内，不应重新起算
    expect(readStoredScrollMap()).toBeNull() // 窗口未到期，尚未落盘

    vi.advanceTimersByTime(THROTTLE_MS)
    expect(readStoredScrollMap()?.['/page-a']?.top).toBe(200)
  })
})

describe('ScrollRestorer · 恢复（双 rAF）', () => {
  it('pathname 切换且有存量值 → 恢复 scrollTop', () => {
    seedScrollMap({
      '/page-b': {
        top: 555,
        touchedAt: Date.now(),
      },
    })
    mockUsePathname.mockReturnValue('/page-a')
    const { rerender } = render(withIdentity(
      <>
        <div data-scroll-container data-testid="main" />
        <ScrollRestorer />
      </>,
    ))
    const main = screen.getByTestId('main')
    flushRaf()
    flushRaf() // 完成 /page-a 挂载时的恢复（无存量，置 0）

    mockUsePathname.mockReturnValue('/page-b')
    rerender(withIdentity(
      <>
        <div data-scroll-container data-testid="main" />
        <ScrollRestorer />
      </>,
    ))

    // 同步阶段（effect 内先 main.scrollTop = target）已设一次，此时应已是存量值
    expect(main.scrollTop).toBe(555)
    // 双 rAF 尽力而为再设两次，最终仍稳定为存量值
    flushRaf()
    flushRaf()
    expect(main.scrollTop).toBe(555)
  })

  it('无存量值 → 挂载时把 scrollTop 显式置 0（不残留切换前的滚动位置）', () => {
    mockUsePathname.mockReturnValue('/no-saved')
    render(<div data-scroll-container data-testid="main" />)
    const main = screen.getByTestId('main')
    main.scrollTop = 888 // 模拟 ScrollRestorer 挂载前遗留的滚动位置

    render(withIdentity(<ScrollRestorer />))

    expect(main.scrollTop).toBe(0)
    flushRaf()
    flushRaf()
    expect(main.scrollTop).toBe(0)
    expect(readStoredScrollMap()).toBeNull()
  })
})

describe('ScrollRestorer · 竞态抑制（restoringRef）', () => {
  it('恢复期 scroll 完全忽略：双 rAF 先完成、150ms 后到也不能覆盖有效目标', () => {
    seedScrollMap({
      '/page-c': {
        top: 300,
        touchedAt: Date.now(),
      },
    })
    mockUsePathname.mockReturnValue('/page-c')
    const main = renderScrollRestorer()
    // 尚未 flushRaf → restoringRef 仍为 true（挂载同步阶段已置位，两帧都没跑完）

    main.scrollTop = 999 // 模拟内容高度未就绪时被浏览器钳制的中间值
    main.dispatchEvent(new Event('scroll'))

    flushRaf()
    flushRaf() // 走完双 rAF，恢复完成，restoringRef=false
    vi.advanceTimersByTime(THROTTLE_MS) // 典型竞态：若恢复期错误起 timer，此时会越过 ref 屏障

    expect(readStoredScrollMap()?.['/page-c']?.top).toBe(300)

    main.scrollTop = 77
    main.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(THROTTLE_MS)

    expect(readStoredScrollMap()?.['/page-c']?.top).toBe(77)
  })
})

describe('ScrollRestorer · 卸载 flush', () => {
  it('卸载时把节流窗口内未落盘的最新滚动位置补写', () => {
    mockUsePathname.mockReturnValue('/page-d')
    const { unmount } = render(withIdentity(
      <>
        <div data-scroll-container data-testid="main" />
        <ScrollRestorer />
      </>,
    ))
    const main = screen.getByTestId('main')
    flushRaf()
    flushRaf()

    main.scrollTop = 42
    main.dispatchEvent(new Event('scroll')) // 触发节流定时器，但未到 150ms 就切走（卸载）

    unmount()

    expect(readStoredScrollMap()?.['/page-d']?.top).toBe(42)
  })
})

describe('ScrollRestorer · sessionStorage 异常降级', () => {
  it('setItem 抛错（如隐私模式配额已满）时 flush 静默降级，不崩溃', () => {
    const setItemSpy = vi.spyOn(window.sessionStorage.__proto__, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError')
    })
    mockUsePathname.mockReturnValue('/page-e')
    const main = renderScrollRestorer()
    flushRaf()
    flushRaf()

    expect(() => {
      main.scrollTop = 10
      main.dispatchEvent(new Event('scroll'))
      vi.advanceTimersByTime(THROTTLE_MS)
    }).not.toThrow()

    setItemSpy.mockRestore()
  })

  it('getItem 抛错时挂载（读存量值）静默降级为空，不崩溃，退化为置 0', () => {
    const getItemSpy = vi.spyOn(window.sessionStorage.__proto__, 'getItem').mockImplementation(() => {
      throw new Error('boom')
    })
    mockUsePathname.mockReturnValue('/page-f')

    expect(() => {
      renderScrollRestorer()
    }).not.toThrow()

    const main = screen.getByTestId('main')
    flushRaf()
    flushRaf()
    expect(main.scrollTop).toBe(0)

    getItemSpy.mockRestore()
  })
})
