import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

// `server-only` 是 Next 编译期环境污染门禁；Vitest 在 Node/jsdom 中测试服务端模块时
// 显式替换为空标记。生产构建仍由 Next 解析真实 marker，模块边界测试也会阻止客户端导入。
vi.mock('server-only', () => ({}))

// 每个用例后卸载组件树，避免 DOM 残留 / 全局 toast store 在用例间串味。
afterEach(() => {
  cleanup()
})

// —— jsdom 缺失、Radix UI（Dialog / AlertDialog）启动时会调用的浏览器 API 垫片 ——
// 不补则一渲染对话框就抛 `hasPointerCapture is not a function` / `matchMedia is not a function`。
// 整段以 typeof window 守卫：node 环境用例（如 use-tab-store.ssr.test.ts 模拟 server bundle）
// 没有 window/Element，setup 直接引用会 ReferenceError；jsdom 用例行为不变。
if (typeof window !== 'undefined') {
  if (!window.matchMedia) {
    window.matchMedia = (query: string) =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList
  }

  if (!('ResizeObserver' in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    }
  }

  Element.prototype.scrollIntoView ??= () => {}
  Element.prototype.hasPointerCapture ??= () => false
  Element.prototype.setPointerCapture ??= () => {}
  Element.prototype.releasePointerCapture ??= () => {}
}
