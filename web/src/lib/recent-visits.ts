import { findNavItemByPath } from '@/lib/nav-utils'
import {
  authenticatedBrowserStoragePolicies,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

// 最近使用记录：按稳定账号 principal 记导航项 href、访问次数与最近访问时间，供仪表盘
// 「最近使用」快捷行。同一账号重新登录可恢复，不同账号即使权限相同也不会互相看到使用画像。
// 只记 nav-config 里存在的功能项（详情子路径归一到所属 item.href），/dashboard 本身不记。
// 同页内写入用自定义事件通知订阅者（原生 storage 事件只跨标签页）。

const CHANGE_EVENT = 'swapmid-recent-visits-change'
/** 只保留最近的条数上限（防止 localStorage 无限膨胀） */
const MAX_ENTRIES = 30

interface VisitEntry {
  count: number
  lastAt: number
}

type VisitMap = Record<string, VisitEntry>

function isVisitMap(value: unknown): value is VisitMap {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const entries = Object.entries(value)
  if (entries.length > 100) return false
  return entries.every(([href, entry]) => {
    if (!href.startsWith('/') || href.length > 2_048) return false
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) return false
    const candidate = entry as Partial<VisitEntry>
    return typeof candidate.count === 'number'
      && Number.isInteger(candidate.count)
      && candidate.count > 0
      && typeof candidate.lastAt === 'number'
      && Number.isFinite(candidate.lastAt)
      && candidate.lastAt > 0
  })
}

function readMap(storage: AuthenticatedBrowserStorage): VisitMap {
  return storage.read(
    authenticatedBrowserStoragePolicies.recentVisits,
    isVisitMap,
  ) ?? {}
}

function writeMap(storage: AuthenticatedBrowserStorage, map: VisitMap): void {
  if (storage.write(authenticatedBrowserStoragePolicies.recentVisits, map)) {
    window.dispatchEvent(new CustomEvent(CHANGE_EVENT, {
      detail: storage.key(authenticatedBrowserStoragePolicies.recentVisits),
    }))
  }
}

function getLockManager(): LockManager | null {
  if (typeof navigator === 'undefined') return null
  try {
    return navigator.locks && typeof navigator.locks.request === 'function'
      ? navigator.locks
      : null
  } catch {
    return null
  }
}

function recordVisitUnlocked(
  storage: AuthenticatedBrowserStorage,
  href: string,
  visitedAt: number,
): void {
  const map = readMap(storage)
  const prev = map[href]
  map[href] = {
    count: (prev?.count ?? 0) + 1,
    // 锁排队顺序不保证等于用户访问时刻；迟到任务不能把更晚访问时间倒退。
    lastAt: Math.max(prev?.lastAt ?? 0, visitedAt),
  }

  // 超上限时删最久未访问的条目
  const keys = Object.keys(map)
  if (keys.length > MAX_ENTRIES) {
    keys
      .sort((a, b) => (map[a]?.lastAt ?? 0) - (map[b]?.lastAt ?? 0))
      .slice(0, keys.length - MAX_ENTRIES)
      .forEach((key) => delete map[key])
  }
  writeMap(storage, map)
}

/** 记录一次访问（在 RouteWatcher 的路由 effect 里调用）。非导航项路径与 /dashboard 自动忽略。 */
export async function recordVisit(
  storage: AuthenticatedBrowserStorage,
  pathname: string,
): Promise<void> {
  if (typeof window === 'undefined') return
  if (pathname === '/dashboard') return
  const item = findNavItemByPath(pathname)
  if (!item) return

  const visitedAt = Date.now()
  const locks = getLockManager()
  if (!locks) {
    // Firefox 旧版/受限 WebView 没有 Web Locks：保持原同步读改写的 best-effort 行为。
    recordVisitUnlocked(storage, item.href, visitedAt)
    return
  }

  // key 已包含 principal namespace；同源不同标签、同账号不同 identity 都竞争同一把锁，
  // 不同账号则互不阻塞。storage 始终由本次调用闭包捕获，迟到回调不会写入新身份。
  const lockName = storage.key(authenticatedBrowserStoragePolicies.recentVisits)
  let mutationCompleted = false
  const mutateOnce = () => {
    if (mutationCompleted) return
    mutationCompleted = true
    recordVisitUnlocked(storage, item.href, visitedAt)
  }

  try {
    await locks.request(lockName, { mode: 'exclusive' }, mutateOnce)
    // 非规范/受限实现若未调用回调便静默 resolve，也要安全降级；迟到回调由 once 闸门去重。
    mutateOnce()
  } catch {
    // request 抛 SecurityError、被浏览器策略拒绝或锁服务异常时，不影响导航主流程。
    // 若回调已执行后 Promise 才 reject，once 闸门避免重复计数。
    mutateOnce()
  }
}

// —— 供 useSyncExternalStore 的订阅/快照（快照按内容缓存，避免每次新数组导致的无限重渲染）——

const snapshotCache = new Map<string, { json: string; value: string[] }>()

/** 按最近访问排序的 href 列表（最多 limit 个）。 */
export function getRecentVisitsSnapshot(
  storage: AuthenticatedBrowserStorage,
  limit = 8,
): string[] {
  const map = readMap(storage)
  const hrefs = Object.entries(map)
    .sort(([, a], [, b]) => b.lastAt - a.lastAt)
    .slice(0, limit)
    .map(([href]) => href)
  const json = JSON.stringify(hrefs)
  const cacheKey = `${storage.key(authenticatedBrowserStoragePolicies.recentVisits)}:recent:${limit}`
  const cached = snapshotCache.get(cacheKey)
  if (!cached || cached.json !== json) {
    snapshotCache.set(cacheKey, { json, value: hrefs })
    return hrefs
  }
  return cached.value
}

/** 按访问频次降序（同频次按最近访问降序）的 href 列表（最多 limit 个，可指定最低访问次数 minCount）。 */
export function getFrequentVisitsSnapshot(
  storage: AuthenticatedBrowserStorage,
  limit = 8,
  minCount = 1,
): string[] {
  const map = readMap(storage)
  const hrefs = Object.entries(map)
    .filter(([, entry]) => entry.count >= minCount)
    .sort(([, a], [, b]) => (b.count - a.count) || (b.lastAt - a.lastAt))
    .slice(0, limit)
    .map(([href]) => href)
  const json = JSON.stringify(hrefs)
  const cacheKey = `${storage.key(authenticatedBrowserStoragePolicies.recentVisits)}:freq:${limit}:${minCount}`
  const cached = snapshotCache.get(cacheKey)
  if (!cached || cached.json !== json) {
    snapshotCache.set(cacheKey, { json, value: hrefs })
    return hrefs
  }
  return cached.value
}

const EMPTY: string[] = []

/** SSR / hydration 首帧快照：恒为空数组（客户端订阅后立即以真实数据重渲染）。 */
export function getServerSnapshot(): string[] {
  return EMPTY
}

export function subscribeRecentVisits(
  storage: AuthenticatedBrowserStorage,
  callback: () => void,
): () => void {
  const storageKey = storage.key(authenticatedBrowserStoragePolicies.recentVisits)
  const handleLocalChange = (event: Event) => {
    if (event instanceof CustomEvent && event.detail === storageKey) callback()
  }
  const handleCrossTabChange = (event: StorageEvent) => {
    if (event.key === storageKey) callback()
  }
  window.addEventListener(CHANGE_EVENT, handleLocalChange)
  window.addEventListener('storage', handleCrossTabChange)
  return () => {
    window.removeEventListener(CHANGE_EVENT, handleLocalChange)
    window.removeEventListener('storage', handleCrossTabChange)
  }
}
