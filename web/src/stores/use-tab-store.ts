import { create, type StateCreator } from 'zustand'
import { createJSONStorage, persist, type PersistOptions, type StateStorage } from 'zustand/middleware'
import {
  authenticatedBrowserStoragePolicies,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

export interface Tab {
  key: string
  title: string
  path: string
}

export const HOME_KEY = '/dashboard'
// i18n 例外登记（批次4k扫描仪表已排查，勿删）：title 是持久化到 sessionStorage 的中文基线
// （与 nav-utils.ts getPageTitle 的 DASHBOARD_PATH 分支同一硬编码值），非直接展示文案——
// tab-bar.tsx/tab-list-menu.tsx 渲染时统一经 resolveTabTitle(tab, t) 现场按当前语言求值，
// 只有 tab.title 偏离此基线（业务层 updateTabTitle 覆盖过）才会原样显示。属「持久化 UI 字符串
// 存原值、渲染时翻」的既定范式（蓝图关键坑节），不是待翻译文案。
export const HOME_TAB: Tab = { key: HOME_KEY, title: '仪表盘', path: HOME_KEY }

interface TabStore {
  tabs: Tab[]
  activeKey: string
  openTab: (tab: Tab) => void
  updateActivePath: (path: string) => void
  updateTabTitle: (key: string, title: string) => void
  closeTab: (key: string) => string
  closeOthers: (key: string) => void
  closeLeft: (key: string) => string
  closeRight: (key: string) => string
  closeAll: () => void
  setActive: (key: string) => void
  setTabsOrder: (next: Tab[]) => void
  purgeUnauthorizedTabs: (isAllowed: (tab: Tab) => boolean) => string
}

// 实际落盘的子集（见下方 partialize）。显式标注让 persist 的持久化类型参数不退化成整个
// TabStore，否则 partialize 会被要求返回完整 store（含方法）而类型不符。
type TabPersistState = Pick<TabStore, 'tabs' | 'activeKey'>

// 显式标注 creator 类型：在 create<TabStore>()(persist(...)) 这种柯里化 + 中间件包裹下，
// set/get 的类型若靠推断会退化成 implicit any（污染 tsc）。用 StateCreator<TabStore, ...>
// 把 set/get 钉死为强类型，与运行时行为无关。
const tabStoreCreator: StateCreator<TabStore, [['zustand/persist', unknown]]> = (set, get) => ({
  tabs: [HOME_TAB],
  activeKey: HOME_KEY,

  // URL 为事实源：已存在的页签除激活外同步刷新 path 为本次进入的完整 URL（含 query）。
  // 配合导航入口统一走存量路径（resolveStoredPagePath），消除「地址栏、页面、页签存档」三处漂移——
  // 带新 query 的定向链接（如工作台待办）进入后页签记住新筛选，裸路径进入即显式回到默认视图。
  openTab: (tab) => {
    const { tabs } = get()
    const exists = tabs.some(t => t.key === tab.key)
    if (exists) {
      set({
        tabs: tabs.map(t => (t.key === tab.key ? { ...t, path: tab.path } : t)),
        activeKey: tab.key,
      })
    } else {
      set({ tabs: [...tabs, tab], activeKey: tab.key })
    }
  },

  // 更新当前激活页签的 path（含最新筛选/分页 query），供「切走再切回」时 router.push 恢复。
  // 由 patchListUrl 在 shallow 改地址栏后同步调用；key(pathname) 不变，只换 path。
  updateActivePath: (path) => {
    set((state) => ({
      tabs: state.tabs.map(t => (t.key === state.activeKey ? { ...t, path } : t)),
    }))
  },

  // 把指定页签标题改成业务名称（§15 轮H）：静态层（getPageTitle）只能给「样品详情」这类通用标题，
  // 详情页数据就绪后（如样品品名、项目名称）调用此 action 精确化，使同类型的多个详情页页签互相区分。
  // 幂等：key 不存在（页签已被关闭/尚未打开）或标题未变均静默跳过，不触发多余的 set/持久化。
  updateTabTitle: (key, title) => {
    const { tabs } = get()
    const idx = tabs.findIndex(t => t.key === key)
    if (idx === -1 || tabs[idx].title === title) return
    set({ tabs: tabs.map(t => (t.key === key ? { ...t, title } : t)) })
  },

  // Returns the key to navigate to after closing
  closeTab: (key) => {
    const { tabs, activeKey } = get()
    if (key === HOME_KEY) return HOME_KEY

    const idx = tabs.findIndex(t => t.key === key)
    if (idx === -1) return activeKey

    const newTabs = tabs.filter(t => t.key !== key)

    if (key !== activeKey) {
      set({ tabs: newTabs })
      return activeKey
    }

    // Closing the active tab — go to nearest remaining
    const nextTab = newTabs[Math.min(idx, newTabs.length - 1)]
    set({ tabs: newTabs, activeKey: nextTab.key })
    return nextTab.key
  },

  closeOthers: (key) => {
    set((state) => ({
      tabs: state.tabs.filter(t => t.key === HOME_KEY || t.key === key),
      activeKey: key,
    }))
  },

  // Returns the key to navigate to (may differ from activeKey if active was in range)
  closeLeft: (key) => {
    const { tabs, activeKey } = get()
    const idx = tabs.findIndex(t => t.key === key)
    if (idx <= 1) return activeKey // nothing to close

    const newTabs = tabs.filter((_, i) => i === 0 || i >= idx)
    const keepActive = newTabs.some(t => t.key === activeKey)
    const nextKey = keepActive ? activeKey : key
    set({ tabs: newTabs, activeKey: nextKey })
    return nextKey
  },

  closeRight: (key) => {
    const { tabs, activeKey } = get()
    const idx = tabs.findIndex(t => t.key === key)
    if (idx >= tabs.length - 1) return activeKey // nothing to close

    const newTabs = tabs.filter((_, i) => i <= idx)
    const keepActive = newTabs.some(t => t.key === activeKey)
    const nextKey = keepActive ? activeKey : key
    set({ tabs: newTabs, activeKey: nextKey })
    return nextKey
  },

  closeAll: () => {
    set({ tabs: [HOME_TAB], activeKey: HOME_KEY })
  },

  setActive: (key) => set({ activeKey: key }),

  // Reorder.Group 的 onReorder 直接给新顺序整个数组，形状对齐 framer-motion 回调（而非旧
  // fromIndex/toIndex）。防御：next 须与现有 tabs 同成员同长度，否则整次 set 忽略。
  // Home 钉死在 0 位——dragListener 已挡 Home 自身发起拖拽，但拖动其右邻页签越过 Home 时，
  // framer-motion 内部按相邻位置交换仍可能把 Home 顶到 1 位，此处兜底纠正。
  setTabsOrder: (next) => {
    const { tabs } = get()
    if (next.length !== tabs.length) return
    const currentKeys = new Set(tabs.map(t => t.key))
    if (new Set(next.map(t => t.key)).size !== tabs.length) return
    if (next.some(t => !currentKeys.has(t.key))) return

    if (next[0]?.key === HOME_KEY) {
      set({ tabs: next })
      return
    }
    const homeTab = next.find(t => t.key === HOME_KEY)
    if (!homeTab) return
    const pinned = [homeTab, ...next.filter(t => t.key !== HOME_KEY)]
    // 钉回后与现序一致则不 set：拖页签压在 Home 中线附近时 framer-motion 会高频回调
    // "换到 Home 前"，每次都被钉回原序，set 新引用只会造成拖拽中无意义的连续重渲染。
    if (pinned.every((t, i) => t.key === tabs[i].key)) return
    set({ tabs: pinned })
  },

  purgeUnauthorizedTabs: (isAllowed) => {
    const { tabs, activeKey } = get()
    const nextTabs = [HOME_TAB]
    for (const tab of tabs) {
      if (tab.key === HOME_KEY) continue
      if (isAllowed(tab) && !nextTabs.some(t => t.key === tab.key)) {
        nextTabs.push(tab)
      }
    }

    const nextActiveKey = nextTabs.some(t => t.key === activeKey) ? activeKey : HOME_KEY
    const changed = nextTabs.length !== tabs.length
      || nextActiveKey !== activeKey
      || nextTabs.some((tab, index) => tab.key !== tabs[index]?.key)

    if (changed) {
      set({ tabs: nextTabs, activeKey: nextActiveKey })
    }

    return nextActiveKey
  },
})

// 页签持久化到「当前身份世代 + 当前浏览器标签页」作用域：
// 整页刷新（F5 / VersionWatcher 版本自愈刷新）后页签原样恢复，不再全部丢失。
// skipHydration：SSR 首帧渲染默认值，挂载后由 RouteWatcher 显式 rehydrate——
// 否则客户端首帧就是恢复后的 N 个页签，与服务端 HTML 不一致触发 hydration mismatch。
// 默认 storage 始终是 serverNoopStorage；RouteWatcher 在 rehydrate 之前用首屏权威 identity
// 替换成 browser-storage-policy 的闭包适配器。这样 store 模块求值期不读取全局 sessionStorage，
// 迟到旧文档也只能写回自己的旧 identity namespace。
// server 端若 storage 缺失，zustand persist 会提前返回、不挂 store.persist API——
// TabBar 渲染期直读 useTabStore.persist.*（水合标志），server 渲染即 TypeError 500
//（带会话硬刷 dashboard 任意路由必现；build 期因 layout force-dynamic 不预渲染而不显形）。
// 兜底一个只读空实现让 persist API 两端恒存在：server 端 hasHydrated() 恒 false，
// 与 tab-bar 的 getServerHydratedSnapshot 一致；客户端由 RouteWatcher 显式绑定真作用域。
// 回归网：use-tab-store.ssr.test.ts（node 环境）钉死 server 端 persist API 存在。
const serverNoopStorage: StateStorage = {
  getItem: () => null,
  setItem: () => {},
  removeItem: () => {},
}

const tabPersistOptions: PersistOptions<TabStore, TabPersistState> = {
  // 只是传给 scoped StateStorage 的子键，不再是浏览器全局键。
  name: 'state',
  storage: createJSONStorage(() => serverNoopStorage),
  partialize: (s) => ({ tabs: s.tabs, activeKey: s.activeKey }),
  skipHydration: true,
}

export const useTabStore = create<TabStore>()(persist(tabStoreCreator, tabPersistOptions))

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** 防止语法合法但形状错误的旧/损坏 JSON 进入 Zustand 后在渲染期触发 `.map/.filter` 异常。 */
function isSerializedTabPersistState(raw: string): boolean {
  try {
    const envelope: unknown = JSON.parse(raw)
    if (!isRecord(envelope) || !isRecord(envelope.state)) return false
    const { tabs, activeKey } = envelope.state
    if (!Array.isArray(tabs) || tabs.length === 0 || tabs.length > 50) return false
    if (typeof activeKey !== 'string' || activeKey.length === 0 || activeKey.length > 2_048) {
      return false
    }

    const keys = new Set<string>()
    for (const tab of tabs) {
      if (!isRecord(tab)) return false
      if (
        typeof tab.key !== 'string'
        || tab.key.length === 0
        || tab.key.length > 2_048
        || typeof tab.title !== 'string'
        || tab.title.length > 256
        || typeof tab.path !== 'string'
        || tab.path.length === 0
        || tab.path.length > 4_096
      ) {
        return false
      }
      keys.add(tab.key)
    }

    return keys.size === tabs.length
      && keys.has(HOME_KEY)
      && keys.has(activeKey)
  } catch {
    return false
  }
}

/**
 * 必须在 `persist.rehydrate()` 前调用。适配器闭包绑定当前文档首屏 identity，
 * 后续任何 Zustand 延迟写入都不会读取“最新全局身份”而串到另一个账号。
 */
export function configureTabStorePersistence(storage: AuthenticatedBrowserStorage): void {
  // 先把持久层临时切到 no-op，再同步复位仍属于旧 identity 的模块级状态。这样复位动作既不会
  // 写坏旧 identity，也不会覆盖新 identity 尚待 rehydrate 的有效存档。
  useTabStore.persist.setOptions({
    storage: createJSONStorage(() => serverNoopStorage),
  })
  useTabStore.setState({ tabs: [HOME_TAB], activeKey: HOME_KEY })

  useTabStore.persist.setOptions({
    storage: createJSONStorage(() =>
      storage.createStateStorage(
        authenticatedBrowserStoragePolicies.tabs,
        isSerializedTabPersistState,
      ),
    ),
  })
}

// ============ 存量路径解析（列表状态 URL 持久化的「入口收口」）============
// 方案 A 把筛选/分页存进页签 path 的 query，但恢复必须由「回到页面的那次导航」携带 query。
// 侧栏/工作台/全局搜索等入口的 href 都是裸路径，直接跳会以默认状态重进页面——
// 统一经此解析：裸路径 → 已开页签的存量 path（含 query）；自带 query 的定向链接原样直达。

/** 一次性解析（点击时机用，如 router.push 前、关页签后的落点导航）。 */
export function resolveStoredPagePath(href: string): string {
  if (href.includes('?')) return href
  return useTabStore.getState().tabs.find(t => t.key === href)?.path ?? href
}

/** 响应式解析（渲染 href 用，页签 path 变化时链接同步更新）。
 * store 配 skipHydration：SSR 与客户端首帧都按默认页签解析出裸 href，无 hydration mismatch；
 * RouteWatcher rehydrate 后自动更新为存量路径。 */
export function useStoredPagePath(href: string): string {
  return useTabStore(s => (href.includes('?') ? href : (s.tabs.find(t => t.key === href)?.path ?? href)))
}
